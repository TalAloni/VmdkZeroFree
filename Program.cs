/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using DiskAccessLibrary.VMDK;
using System;
using System.Diagnostics;
using System.IO;
using VmdkZeroFree.Ext4;

namespace VmdkZeroFree
{
    public class Program
    {
        private const byte LinuxRaidPartitionType = 0xFD;
        private const byte Ext2PartitionType = 0x83;
        private const int BlockSizeInSectors = 8;

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
            VirtualMachineDisk inputDiskImage = new VirtualMachineDisk(sourcePath);
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

        private static void TrimUnusedBlocks(VirtualMachineDisk inputDiskImage, VirtualMachineDisk outputDiskImage)
        {
            inputDiskImage.ExclusiveLock();
            outputDiskImage.ExclusiveLock();

            TrimmableDisk workDisk = new TrimmableDisk(inputDiskImage, BlockSizeInSectors);
            MasterBootRecord mbr = new MasterBootRecord(workDisk.ReadSector(0));
            
            for (int partitionTableEntryIndex = 0; partitionTableEntryIndex < 4; partitionTableEntryIndex++)
            {
                PartitionTableEntry partitionTableEntry = mbr.PartitionTable[partitionTableEntryIndex];
                if (partitionTableEntry.PartitionType == LinuxRaidPartitionType ||
                    partitionTableEntry.PartitionType == Ext2PartitionType)
                {
                    DiskExtent volumeData = LinuxLvmHelper.GetUnderlyingVolumeData(workDisk, partitionTableEntry);
                    if (volumeData.TotalSectors > 0)
                    {
                        TrimUnusedBlocks(volumeData);
                    }
                }
            }

            Copy(workDisk, 0, outputDiskImage, 0, workDisk.TotalSectors);
            inputDiskImage.ReleaseLock();
            outputDiskImage.ReleaseLock();
        }

        private static void TrimUnusedBlocks(DiskExtent volumeData)
        {
            byte[] buffer = volumeData.ReadSectors(2, 2);
            Ext4SuperBlock superBlock = Ext4SuperBlock.ReadExt4SuperBlock(buffer);
            if (superBlock != null)
            {
                Ext4Processor.TrimUnusedBlocks(volumeData, superBlock);
            }
        }

        private static void Copy(Disk disk1, long disk1Offset, Disk disk2, long disk2Offset, long sectorCount)
        {
            int readSizeInSectors = 2048;
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (long index = 0; index < sectorCount; index += readSizeInSectors)
            {
                long sectorsLeftToRead = sectorCount - index;
                int sectorsToRead = (int)Math.Min(readSizeInSectors, sectorsLeftToRead);
                byte[] data = disk1.ReadSectors(disk1Offset + index, sectorsToRead);
                disk2.WriteSectors(disk2Offset + index, data);
                string status = $"Written {index * disk1.BytesPerSector / 1024 / 1024} MB to virtual disk";
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
    }
}
