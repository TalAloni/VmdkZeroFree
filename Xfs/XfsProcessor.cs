/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using System;
using System.Collections.Generic;

namespace VmdkZeroFree.Xfs
{
    public static class XfsProcessor
    {
        public static void TrimUnusedBlocks(DiskExtent volume, XfsSuperBlock superBlock)
        {
            for (int allocationGroupIndex = 0; allocationGroupIndex < superBlock.AGCount; allocationGroupIndex++)
            {
                int sectorsPerBlock = (int)(superBlock.BlockSize / volume.BytesPerSector);
                long allocationGroupStartSector = allocationGroupIndex * superBlock.AGBlocks * sectorsPerBlock;
                long freeSpaceBlockSectorIndex = allocationGroupStartSector + 1;
                byte[] buffer = volume.ReadSector(freeSpaceBlockSectorIndex);
                AGFreeSpaceBlock freeSpaceBlock = AGFreeSpaceBlock.ReadAGFreeSpaceBlock(buffer);

                List<KeyValuePair<uint, uint>> entries = BTreeReader.ReadFreeSpaceByBlockBTreeEntries(volume, allocationGroupStartSector, freeSpaceBlock.FreespaceByBlockRootBlockNumber, superBlock);
                foreach (KeyValuePair<uint, uint> entry in entries)
                {
                    if (volume.Disk is TrimmableDisk disk)
                    {
                        uint blockStart = entry.Key;
                        uint blockCount = entry.Value;
                        if (blockCount * sectorsPerBlock > Int32.MaxValue)
                        {
                            throw new NotImplementedException();
                        }
                        disk.TrimBlocks(volume.FirstSector + allocationGroupStartSector + blockStart * sectorsPerBlock, (int)blockCount * sectorsPerBlock);
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
