﻿/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using DiskAccessLibrary.VMDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Utilities;
using VmdkZeroFree.Ext4;
using VmdkZeroFree.Lvm;
using VmdkZeroFree.Xfs;

namespace VmdkZeroFree
{
    public class Program
    {
        private const byte LinuxRaidPartitionType = 0xFD;
        private const byte LinuxNativePartitionType = 0x83; // EXT2/EXT3/EXT4/XFS
        private const int BlockSizeInSectors = 8;
        private const int QueueSize = 16;

        private static readonly Guid LinuxDataPartitionTypeGuid = new Guid("0FC63DAF-8483-4772-8E79-3D69D8477DE4");

        static int Main(string[] args)
        {
            Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"VmdkZeroFree v{version.ToString(3)}");
            if (args.Length < 2)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine($"VmdkZeroFree <input-vmdk-path> <output-vmdk-path> [-disk-type <disk-type>]");
                Console.WriteLine($"VmdkZeroFree <input-vmdk-path> <output-vmdk-path> [-max-compression]");
                Console.WriteLine();
                Console.WriteLine("  Disk types:");
                Console.WriteLine("    monolithic-sparse");
                Console.WriteLine("    monolithic-flat");
                Console.WriteLine("    stream-optimized (default)");
                return 0;
            }

            VirtualMachineDiskType diskType = VirtualMachineDiskType.StreamOptimized;
            bool useFastestCompression = true;
            if (args.Length == 3 && args[2] == "-max-compression")
            {
                useFastestCompression = false;
            }
            else if (args.Length == 4 && args[2] == "-disk-type")
            {
                string diskTypeString = args[3];
                try
                {
                    diskType = ParseDiskType(diskTypeString);
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("Invalid disk-type");
                    return -1;
                }
            }
            else if (args.Length != 2)
            {
                Console.WriteLine("Invalid arguments");
                return -1;
            }

            string sourcePath = args[0];
            string outputPath = args[1];

            if (!File.Exists(sourcePath))
            {
                Console.WriteLine($"Error: Input file '{sourcePath}' does not exist");
                return -1;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            TrimUnusedBlocks(sourcePath, outputPath, diskType, useFastestCompression);
            stopwatch.Stop();
            long sizeBeforeInMb = new FileInfo(sourcePath).Length / 1024 / 1024;
            long sizeAfterInMb = new FileInfo(outputPath).Length / 1024 / 1024;

            string status = $"Took {(int)stopwatch.Elapsed.TotalSeconds} seconds.";
            if (!Console.IsOutputRedirected)
            {
                Console.WriteLine(status.PadRight(Console.WindowWidth - 1));
            }
            else
            {
                Console.WriteLine(status);
            }
            Console.WriteLine($"Input size: {sizeBeforeInMb.ToString("#,##0")} MB");
            Console.WriteLine($"Output size: {sizeAfterInMb.ToString("#,##0")} MB");
            return 0;
        }

        private static VirtualMachineDiskType ParseDiskType(string diskTypeString)
        {
            switch (diskTypeString.ToLower())
            {
                case "monolithic-sparse":
                    return VirtualMachineDiskType.MonolithicSparse;
                case "monolithic-flat":
                    return VirtualMachineDiskType.MonolithicFlat;
                case "stream-optimized":
                    return VirtualMachineDiskType.StreamOptimized;
                default:
                    throw new ArgumentException("Invalid disk type");
            }
        }

        private static void TrimUnusedBlocks(string sourcePath, string outputPath, VirtualMachineDiskType diskType, bool useFastestCompression)
        {
            VirtualMachineDisk inputDiskImage = new VirtualMachineDisk(sourcePath, true);
            if (inputDiskImage.DiskType == VirtualMachineDiskType.StreamOptimized &&
                diskType == VirtualMachineDiskType.StreamOptimized)
            {
                TrimStreamOptimizedVmdk(inputDiskImage, outputPath, !useFastestCompression);
            }
            else
            {
                VirtualMachineDisk outputDiskImage;
                if (diskType == VirtualMachineDiskType.MonolithicSparse)
                {
                    outputDiskImage = VirtualMachineDisk.CreateMonolithicSparse(outputPath, inputDiskImage.Size);
                }
                else if (diskType == VirtualMachineDiskType.MonolithicFlat)
                {
                    outputDiskImage = VirtualMachineDisk.CreateMonolithicFlat(outputPath, inputDiskImage.Size);
                }
                else
                {
                    outputDiskImage = VirtualMachineDisk.CreateStreamOptimized(outputPath, inputDiskImage.Size, useFastestCompression);
                }

                TrimUnusedBlocks(inputDiskImage, outputDiskImage);
            }
        }

        private static void TrimUnusedBlocks(VirtualMachineDisk inputDiskImage, VirtualMachineDisk outputDiskImage)
        {
            inputDiskImage.ExclusiveLock();
            outputDiskImage.ExclusiveLock();

            TrimmableDisk workDisk = new TrimmableDisk(inputDiskImage, BlockSizeInSectors);
            TrimUnusedBlocks(workDisk);
            Copy(workDisk, 0, outputDiskImage, 0, workDisk.TotalSectors);

            inputDiskImage.ReleaseLock();
            outputDiskImage.ReleaseLock();
        }

        private static void TrimUnusedBlocks(TrimmableDisk disk)
        {
            List<Partition> partitions = BasicDiskHelper.GetPartitions(disk);
            foreach (Partition partition in partitions)
            {
                if ((partition is MBRPartition mbrPartition && (mbrPartition.PartitionType == LinuxRaidPartitionType || mbrPartition.PartitionType == LinuxNativePartitionType)) ||
                    (partition is GPTPartition gptPartition && (gptPartition.TypeGuid == LinuxDataPartitionTypeGuid)))
                {
                    List<DiskExtent> volumes = LinuxLvmHelper.GetUnderlyingVolumes(disk, partition);
                    foreach (DiskExtent volume in volumes)
                    {
                        if (volume.TotalSectors > 0)
                        {
                            TrimUnusedBlocks(volume);
                        }
                    }
                }
            }
        }

        private static void TrimUnusedBlocks(DiskExtent volumeData)
        {
            byte[] buffer = volumeData.ReadSector(0);
            XfsSuperBlock xfsSuperBlock = XfsSuperBlock.ReadXfsSuperBlock(buffer);
            if (xfsSuperBlock != null)
            {
                Console.WriteLine($"XFS file system detected at lba {volumeData.FirstSector}");
                XfsProcessor.TrimUnusedBlocks(volumeData, xfsSuperBlock);
                return;
            }

            buffer = volumeData.ReadSectors(2, 2);
            Ext4SuperBlock ext4SuperBlock = Ext4SuperBlock.ReadExt4SuperBlock(buffer);
            if (ext4SuperBlock != null)
            {
                Console.WriteLine($"EXT file system detected at lba {volumeData.FirstSector}");
                Ext4Processor.TrimUnusedBlocks(volumeData, ext4SuperBlock);
            }
        }

        private static void Copy(Disk disk1, long disk1Offset, Disk disk2, long disk2Offset, long sectorCount)
        {
            BlockingQueue<byte[]> writeQueue = new BlockingQueue<byte[]>();
            new Thread(delegate ()
            {
                Copy(disk1, disk1Offset, sectorCount, writeQueue);
            }).Start();

            Stopwatch stopwatch = Stopwatch.StartNew();
            long writeOffset = 0;
            while (writeQueue.TryDequeue(out byte[] data))
            {
                disk2.WriteSectors(disk2Offset + writeOffset, data);
                writeOffset += data.Length / disk1.BytesPerSector;
                string status = $"Written {writeOffset * disk1.BytesPerSector / 1024 / 1024} MB to virtual disk";
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
        }

        private static void Copy(Disk disk1, long disk1Offset, long sectorCount, BlockingQueue<byte[]> writeQueue)
        {
            int readSizeInSectors = 2048;
            for (long index = 0; index < sectorCount; index += readSizeInSectors)
            {
                while (writeQueue.Count > QueueSize)
                {
                    Thread.Sleep(1);
                }
                long sectorsLeftToRead = sectorCount - index;
                int sectorsToRead = (int)Math.Min(readSizeInSectors, sectorsLeftToRead);
                byte[] data = disk1.ReadSectors(disk1Offset + index, sectorsToRead);
                writeQueue.Enqueue(data);
            }

            writeQueue.Stop();
        }

        public static void TrimStreamOptimizedVmdk(VirtualMachineDisk inputDiskImage, string outputPath, bool forceMaxCompression)
        {
            TrimmableDisk workDisk = new TrimmableDisk(inputDiskImage, BlockSizeInSectors);
            inputDiskImage.ExclusiveLock();
            TrimUnusedBlocks(workDisk);
            inputDiskImage.ReleaseLock();

            RawDiskImage inputDisk = new RawDiskImage(inputDiskImage.Path, true);
            inputDisk.ExclusiveLock();
            DiskImageReader inputDiskReader = new DiskImageReader(inputDisk);

            DiskImage outputDisk = RawDiskImage.Create(outputPath, 0);
            outputDisk.ExclusiveLock();
            DiskImageWriter outputDiskWriter = new DiskImageWriter(outputDisk);
            StreamOptimizedVmdkTrimmer.CopyStreamOptimizedVmdk(inputDiskReader, workDisk, outputDiskWriter, forceMaxCompression);

            inputDisk.ReleaseLock();
            outputDisk.ReleaseLock();
        }
    }
}
