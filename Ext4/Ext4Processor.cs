/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VmdkZeroFree.Ext4
{
    public static class Ext4Processor
    {
        public static void TrimUnusedBlocks(DiskExtent volume)
        {
            byte[] buffer = volume.ReadSectors(2, 2);
            Ext4SuperBlock superBlock = Ext4SuperBlock.ReadExt4SuperBlock(buffer);
            if (superBlock != null)
            {
                TrimUnusedBlocks(volume, superBlock);
            }
            else
            {
                throw new NotSupportedException("File system is not supported");
            }
        }

        public static void TrimUnusedBlocks(DiskExtent volume, Ext4SuperBlock superBlock)
        {
            int dataReadSizeInBlocks = 2048 * 512 / superBlock.BlockSize; // 1MiB, 256 x 4KiB (16 grains)
            List<BlockGroupDescriptor> descriptors = ReadBlockGroupDescriptors(volume, superBlock);
            int numberOfBlocksInLastGroup = (int)(superBlock.BlocksCount - (descriptors.Count - 1) * superBlock.BlocksPerGroup);
            int blockSizeInSectors = superBlock.BlockSize / volume.BytesPerSector;
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int groupIndex = 0; groupIndex < descriptors.Count; groupIndex++)
            {
                string status = $"Processing block group {groupIndex} / {descriptors.Count}";
                if (!Console.IsOutputRedirected)
                {
                    Console.WriteLine(status);
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                }
                else if (stopwatch.Elapsed.Seconds > 5)
                {
                    Console.WriteLine(status);
                    stopwatch.Restart();
                }
                
                BlockGroupDescriptor descriptor = descriptors[groupIndex];
                int groupBitmapSizeInBytes = (int)(superBlock.BlocksPerGroup / 8);
                int groupBitmapSizeInSectors = (int)Math.Ceiling((double)groupBitmapSizeInBytes / volume.BytesPerSector);
                byte[] groupBitmap = volume.ReadSectors((long)descriptor.BlockBitmapLocation * blockSizeInSectors, groupBitmapSizeInSectors);
                int blocksInGroup = groupIndex < descriptors.Count - 1 ? (int)superBlock.BlocksPerGroup : numberOfBlocksInLastGroup;
                
                for (int blockIndexInGroup = 0; blockIndexInGroup < blocksInGroup; blockIndexInGroup += dataReadSizeInBlocks)
                {
                    ulong blockIndexInVolume = (ulong)groupIndex * superBlock.BlocksPerGroup + (uint)blockIndexInGroup;
                    long sectorIndexInVolume = (long)blockIndexInVolume * blockSizeInSectors;
                    for (int blockoffset = 0; blockoffset < dataReadSizeInBlocks; blockoffset++)
                    {
                        if (IsBitClear(groupBitmap, blockIndexInGroup + blockoffset))
                        {
                            if (volume.Disk is TrimmableDisk disk)
                            {
                                disk.TrimBlocks(volume.FirstSector + sectorIndexInVolume + blockoffset * blockSizeInSectors, blockSizeInSectors);
                            }
                            else
                            {
                                throw new NotSupportedException();
                            }
                        }
                    }
                }
            }
        }

        private static List<BlockGroupDescriptor> ReadBlockGroupDescriptors(DiskExtent volume, Ext4SuperBlock superBlock)
        {
            int descriptorSize = superBlock.GroupDescriptorSize;
            int groupCount = (int)Math.Ceiling((double)superBlock.BlocksCount / superBlock.BlocksPerGroup);
            int bytesToRead = groupCount * descriptorSize;
            int sectorsToRead = (int)Math.Ceiling((double)bytesToRead / volume.BytesPerSector);
            long groupDescTableOffsetInBytes = (superBlock.FirstDataBlock + 1) * superBlock.BlockSize;
            long groupDescTableOffsetInSectors = groupDescTableOffsetInBytes / volume.BytesPerSector;
            List<BlockGroupDescriptor> descriptors = new List<BlockGroupDescriptor>(groupCount);
            byte[] buffer = volume.ReadSectors(groupDescTableOffsetInSectors, sectorsToRead);
            for (int group = 0; group < groupCount; group++)
            {
                BlockGroupDescriptor descriptor = new BlockGroupDescriptor(buffer, group * descriptorSize, descriptorSize);
                descriptors.Add(descriptor);
            }

            return descriptors;
        }

        private static bool IsBitClear(byte[] bitmap, int bitOffsetInBitmap)
        {
            int byteOffset = bitOffsetInBitmap / 8;
            int bitOffsetInByte = bitOffsetInBitmap % 8;
            bool isInUse = ((bitmap[byteOffset] >> bitOffsetInByte) & 0x01) != 0;
            return !isInUse;
        }
    }
}
