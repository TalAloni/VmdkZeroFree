/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary.VMDK;
using System;
using System.Diagnostics;
using Utilities;

namespace VmdkZeroFree
{
    public static class StreamOptimizedVmdkTrimmer
    {
        private const int BytesPerSector = 512;

        private const uint MarkerEOS = 0;
        private const uint MarkerGT = 1;
        private const uint MarkerGD = 2;
        private const uint MarkerFooter = 3;

        public static void CopyStreamOptimizedVmdk(DiskImageReader inputDiskReader, TrimmableDisk workDisk, DiskImageWriter outputDiskWriter)
        {
            byte[] headerBytes = inputDiskReader.ReadSector();
            SparseExtentHeader header = new SparseExtentHeader(headerBytes);

            int dataStartSector = (int)header.OverHead;
            byte[] metadata = ByteUtils.Concatenate(headerBytes, inputDiskReader.ReadSectors(dataStartSector - 1));
            outputDiskWriter.Write(metadata);

            ulong numberOfGrains = header.Capacity / header.GrainSize;
            int numberOfGrainTables = (int)Math.Ceiling((double)numberOfGrains / header.NumGTEsPerGT);
            byte[] grainDirectory = new byte[numberOfGrainTables * 4];
            byte[] nextGrainTable = null;
            int indexOfNextGrainTableToWrite = 0;
            long grainDirectorySectorIndex = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (inputDiskReader.Position < inputDiskReader.TotalSectors)
            {
                byte[] sectorBytes = inputDiskReader.ReadSector();
                uint compressedSize = LittleEndianConverter.ToUInt32(sectorBytes, 8);
                if (compressedSize > 0)
                {
                    ulong lba = LittleEndianConverter.ToUInt32(sectorBytes, 0);

                    int grainMarkerSize = 12;
                    int sizeInBytes = grainMarkerSize + (int)compressedSize;
                    int sizeInSectors = (int)Math.Ceiling((double)sizeInBytes / 512);
                    byte[] data = ByteUtils.Concatenate(sectorBytes, inputDiskReader.ReadSectors(sizeInSectors - 1));

                    long grainIndex = (long)(lba / header.GrainSize);
                    int grainTableIndex = (int)(grainIndex / header.NumGTEsPerGT); // The index in the grain directory
                    if (grainTableIndex > indexOfNextGrainTableToWrite)
                    {
                        // Commit grain table
                        if (nextGrainTable != null)
                        {
                            // The grain directory entry points to the table, not the marker
                            LittleEndianWriter.WriteUInt32(grainDirectory, indexOfNextGrainTableToWrite * 4, (uint)outputDiskWriter.Position + 1);

                            WriteGrainTable(outputDiskWriter, nextGrainTable);
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
                        LittleEndianWriter.WriteUInt32(nextGrainTable, grainIndexInGrainTable * 4, (uint)outputDiskWriter.Position);

                        if (isCopiedAsIs)
                        {
                            outputDiskWriter.Write(data);
                        }
                        else if (isPartialTrim)
                        {
                            byte[] decompressedData = ZLibCompressionHelper.Decompress(data, grainMarkerSize, (int)compressedSize, (int)header.GrainSize * 512);
                            byte[] trimmedData = workDisk.ApplyTrim(decompressedData, (long)lba, (int)header.GrainSize);
                            bool useFastestCompression = (sectorBytes[grainMarkerSize + 1] == 0x01);
                            byte[] compressedBytes = ZLibCompressionHelper.Compress(trimmedData, 0, trimmedData.Length, useFastestCompression);

                            byte[] grainBytes = GetGrainBytes((long)lba, compressedBytes);
                            outputDiskWriter.Write(grainBytes);
                        }
                    }

                    string status = $"Written {lba * BytesPerSector / 1024 / 1024} MB to virtual disk";
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
                else
                {
                    uint type = LittleEndianConverter.ToUInt32(sectorBytes, 12);
                    ulong sizeInSectors = LittleEndianConverter.ToUInt64(sectorBytes, 0);
                    byte[] data = inputDiskReader.ReadSectors((int)sizeInSectors);
                    // Note: We ignore grain tables and write our own
                    
                    if (type == MarkerGD)
                    {
                        if (nextGrainTable != null)
                        {
                            // The grain directory entry points to the table, not the marker
                            LittleEndianWriter.WriteUInt32(grainDirectory,indexOfNextGrainTableToWrite * 4, (uint)outputDiskWriter.Position + 1);

                            WriteGrainTable(outputDiskWriter, nextGrainTable);
                            nextGrainTable = null;
                        }

                        // GDOffset points to the grain directory, not the marker
                        grainDirectorySectorIndex = outputDiskWriter.Position + 1;
                        WriteGrainDirectory(outputDiskWriter, grainDirectory);
                    }
                    else if (type == MarkerFooter)
                    {
                        header.GDOffset = (ulong)grainDirectorySectorIndex;
                        outputDiskWriter.Write(sectorBytes);
                        outputDiskWriter.Write(header.GetBytes());
                    }
                    else if (type == MarkerEOS)
                    {
                        outputDiskWriter.Write(sectorBytes);
                    }
                }
            }

            outputDiskWriter.Flush();
        }

        private static void WriteGrainTable(DiskImageWriter diskImageWriter, byte[] grainTable)
        {
            byte[] grainTableWithMarker = new byte[BytesPerSector + grainTable.Length];
            LittleEndianWriter.WriteUInt64(grainTableWithMarker, 0, (ulong)(grainTable.Length / BytesPerSector));
            LittleEndianWriter.WriteUInt32(grainTableWithMarker, 12, MarkerGT);
            ByteWriter.WriteBytes(grainTableWithMarker, BytesPerSector, grainTable);
            
            diskImageWriter.Write(grainTableWithMarker);
        }

        private static void WriteGrainDirectory(DiskImageWriter diskImageWriter, byte[] grainDirectory)
        {
            int grainDirectorySectorCount = (int)Math.Ceiling((double)grainDirectory.Length / BytesPerSector);
            byte[] grainDirectoryWithMarker = new byte[(1 + grainDirectorySectorCount) * BytesPerSector];
            LittleEndianWriter.WriteUInt64(grainDirectoryWithMarker, 0, (ulong)grainDirectorySectorCount);
            LittleEndianWriter.WriteUInt32(grainDirectoryWithMarker, 12, MarkerGD);
            ByteWriter.WriteBytes(grainDirectoryWithMarker, BytesPerSector, grainDirectory);

            diskImageWriter.Write(grainDirectoryWithMarker);
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
