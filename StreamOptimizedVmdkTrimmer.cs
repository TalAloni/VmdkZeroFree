/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary.VMDK;
using System;
using System.Diagnostics;
using System.Threading;
using Utilities;

namespace VmdkZeroFree
{
    public static class StreamOptimizedVmdkTrimmer
    {
        private const int BytesPerSector = 512;
        private const int QueueSize = 256;

        private const uint MarkerEOS = 0;
        private const uint MarkerGT = 1;
        private const uint MarkerGD = 2;
        private const uint MarkerFooter = 3;

        public static void CopyStreamOptimizedVmdk(DiskImageReader inputDiskReader, TrimmableDisk workDisk, DiskImageWriter outputDiskWriter, bool forceMaxCompression)
        {
            byte[] headerBytes = inputDiskReader.ReadSector();
            SparseExtentHeader header = new SparseExtentHeader(headerBytes);

            int dataStartSector = (int)header.OverHead;
            byte[] metadata = ByteUtils.Concatenate(headerBytes, inputDiskReader.ReadSectors(dataStartSector - 1));
            outputDiskWriter.Write(metadata);

            BlockingQueue<byte[]> processingQueue = new BlockingQueue<byte[]>();
            new Thread(delegate()
                {
                    FillProcessingQueue(inputDiskReader, processingQueue);
                }
            ).Start();

            BlockingQueue<byte[]> writeQueue = new BlockingQueue<byte[]>();
            new Thread(delegate ()
            {
                CopyStreamOptimizedVmdkData(header, processingQueue, workDisk, writeQueue, forceMaxCompression);
            }
            ).Start();

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (writeQueue.TryDequeue(out byte[] data))
            {
                outputDiskWriter.Write(data);
                string status = $"Written {outputDiskWriter.Position * BytesPerSector / 1024 / 1024} MB to VMDK";
                if (!Console.IsOutputRedirected)
                {
                    Console.WriteLine(status.PadRight(Console.WindowWidth - 1));
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                }
                else if (stopwatch.Elapsed.Seconds > 5)
                {
                    Console.WriteLine(status);
                    stopwatch.Restart();
                }
            }

            outputDiskWriter.Flush();
        }

        private static void FillProcessingQueue(DiskImageReader inputDiskReader, BlockingQueue<byte[]> processingQueue)
        {
            while (inputDiskReader.Position < inputDiskReader.TotalSectors)
            {
                while (processingQueue.Count > QueueSize)
                {
                    Thread.Sleep(1);
                }
                byte[] sectorBytes = inputDiskReader.ReadSector();
                uint compressedSize = LittleEndianConverter.ToUInt32(sectorBytes, 8);
                int additionalNumberOfSectorsToRead;
                if (compressedSize > 0)
                {
                    int grainMarkerSize = 12;
                    int sizeInBytes = grainMarkerSize + (int)compressedSize;
                    int sizeInSectors = (int)Math.Ceiling((double)sizeInBytes / 512);
                    additionalNumberOfSectorsToRead = sizeInSectors - 1;
                }
                else
                {
                    additionalNumberOfSectorsToRead = (int)LittleEndianConverter.ToUInt64(sectorBytes, 0);
                }
                byte[] data = ByteUtils.Concatenate(sectorBytes, inputDiskReader.ReadSectors(additionalNumberOfSectorsToRead));
                processingQueue.Enqueue(data);
            }

            processingQueue.Stop();
        }

        private static void CopyStreamOptimizedVmdkData(SparseExtentHeader header, BlockingQueue<byte[]> processingQueue, TrimmableDisk workDisk, BlockingQueue<byte[]> writeQueue, bool forceMaxCompression)
        {
            ulong numberOfGrains = header.Capacity / header.GrainSize;
            int numberOfGrainTables = (int)Math.Ceiling((double)numberOfGrains / header.NumGTEsPerGT);
            byte[] grainDirectory = new byte[numberOfGrainTables * 4];
            byte[] nextGrainTable = null;
            int indexOfNextGrainTableToWrite = 0;
            long grainDirectorySectorIndex = 0;
            long position = (long)header.OverHead;

            while (processingQueue.TryDequeue(out byte[] data))
            {
                uint compressedSize = LittleEndianConverter.ToUInt32(data, 8);
                if (compressedSize > 0)
                {
                    ulong lba = LittleEndianConverter.ToUInt32(data, 0);
                    int grainMarkerSize = 12;

                    long grainIndex = (long)(lba / header.GrainSize);
                    int grainTableIndex = (int)(grainIndex / header.NumGTEsPerGT); // The index in the grain directory
                    if (grainTableIndex > indexOfNextGrainTableToWrite)
                    {
                        // Commit grain table
                        if (nextGrainTable != null)
                        {
                            // The grain directory entry points to the table, not the marker
                            LittleEndianWriter.WriteUInt32(grainDirectory, indexOfNextGrainTableToWrite * 4, (uint)position + 1);

                            WriteGrainTable(writeQueue, nextGrainTable, ref position);
                            nextGrainTable = null;
                        }

                        indexOfNextGrainTableToWrite = grainTableIndex;
                    }

                    bool? trimStatus = workDisk.IsTrimmable((long)lba, (int)header.GrainSize);
                    bool isCopiedAsIs = trimStatus.HasValue && !trimStatus.Value;
                    bool isPartialTrim = !trimStatus.HasValue;
                    if (isCopiedAsIs || isPartialTrim)
                    {
                        if (nextGrainTable == null)
                        {
                            nextGrainTable = new byte[(int)header.NumGTEsPerGT * 4];
                        }

                        int grainIndexInGrainTable = (int)(grainIndex % header.NumGTEsPerGT);
                        LittleEndianWriter.WriteUInt32(nextGrainTable, grainIndexInGrainTable * 4, (uint)position);

                        if (isCopiedAsIs && !forceMaxCompression)
                        {
                            EnqueueWrite(writeQueue, data, ref position);
                        }
                        else if (isPartialTrim || (isCopiedAsIs && forceMaxCompression))
                        {
                            byte[] decompressedData = ZLibCompressionHelper.Decompress(data, grainMarkerSize, (int)compressedSize, (int)header.GrainSize * 512);
                            byte[] dataToCompress = isPartialTrim ? workDisk.ApplyTrim(decompressedData, (long)lba, (int)header.GrainSize) : decompressedData;
                            bool useFastestCompression = forceMaxCompression ? false : (data[grainMarkerSize + 1] == 0x01);
                            byte[] compressedBytes = ZLibCompressionHelper.Compress(dataToCompress, 0, dataToCompress.Length, useFastestCompression);

                            byte[] grainBytes = GetGrainBytes((long)lba, compressedBytes);
                            EnqueueWrite(writeQueue, grainBytes, ref position);
                        }
                    }
                }
                else
                {
                    uint type = LittleEndianConverter.ToUInt32(data, 12);
                    
                    // Note: We ignore grain tables and write our own
                    if (type == MarkerGD)
                    {
                        if (nextGrainTable != null)
                        {
                            // The grain directory entry points to the table, not the marker
                            LittleEndianWriter.WriteUInt32(grainDirectory, indexOfNextGrainTableToWrite * 4, (uint)position + 1);

                            WriteGrainTable(writeQueue, nextGrainTable, ref position);
                            nextGrainTable = null;
                        }

                        // GDOffset points to the grain directory, not the marker
                        grainDirectorySectorIndex = position + 1;
                        WriteGrainDirectory(writeQueue, grainDirectory, ref position);
                    }
                    else if (type == MarkerFooter)
                    {
                        header.GDOffset = (ulong)grainDirectorySectorIndex;
                        EnqueueWrite(writeQueue, ByteReader.ReadBytes(data, 0, BytesPerSector), ref position);
                        EnqueueWrite(writeQueue, header.GetBytes(), ref position);
                    }
                    else if (type == MarkerEOS)
                    {
                        EnqueueWrite(writeQueue, data, ref position);
                    }
                }
            }

            writeQueue.Stop();
        }

        private static void EnqueueWrite(BlockingQueue<byte[]> writeQueue, byte[] data, ref long position)
        {
            while (writeQueue.Count > QueueSize)
            {
                Thread.Sleep(1);
            }
            writeQueue.Enqueue(data);
            position += data.Length / BytesPerSector;
        }

        private static void WriteGrainTable(BlockingQueue<byte[]> writeQueue, byte[] grainTable, ref long position)
        {
            byte[] grainTableWithMarker = new byte[BytesPerSector + grainTable.Length];
            LittleEndianWriter.WriteUInt64(grainTableWithMarker, 0, (ulong)(grainTable.Length / BytesPerSector));
            LittleEndianWriter.WriteUInt32(grainTableWithMarker, 12, MarkerGT);
            ByteWriter.WriteBytes(grainTableWithMarker, BytesPerSector, grainTable);

            EnqueueWrite(writeQueue, grainTableWithMarker, ref position);
        }

        private static void WriteGrainDirectory(BlockingQueue<byte[]> writeQueue, byte[] grainDirectory, ref long position)
        {
            int grainDirectorySectorCount = (int)Math.Ceiling((double)grainDirectory.Length / BytesPerSector);
            byte[] grainDirectoryWithMarker = new byte[(1 + grainDirectorySectorCount) * BytesPerSector];
            LittleEndianWriter.WriteUInt64(grainDirectoryWithMarker, 0, (ulong)grainDirectorySectorCount);
            LittleEndianWriter.WriteUInt32(grainDirectoryWithMarker, 12, MarkerGD);
            ByteWriter.WriteBytes(grainDirectoryWithMarker, BytesPerSector, grainDirectory);

            EnqueueWrite(writeQueue, grainDirectoryWithMarker, ref position);
        }

        private static byte[] GetGrainBytes(long sectorIndex, byte[] compressedData)
        {
            int markerLength = 12;
            int paddedLengthInSectors = (int)Math.Ceiling((double)(markerLength + compressedData.Length) / BytesPerSector);
            int paddedLengthInBytes = paddedLengthInSectors * BytesPerSector;
            byte[] buffer = new byte[paddedLengthInBytes];
            LittleEndianWriter.WriteUInt64(buffer, 0, (ulong)sectorIndex);
            LittleEndianWriter.WriteUInt32(buffer, 8, (uint)compressedData.Length);
            ByteWriter.WriteBytes(buffer, 12, compressedData);

            return buffer;
        }
    }
}
