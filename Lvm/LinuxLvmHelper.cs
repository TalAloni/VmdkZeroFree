/* Copyright (C) 2023-2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using Utilities;

namespace VmdkZeroFree.Lvm
{
    public class LinuxLvmHelper
    {
        private const uint LvmSignature = 0xA92B4EFC;
        private const ulong LvmLabelSignature = 0x454E4F4C4542414C; // "LABELONE"
        private const byte LinuxRaidPartitionType = 0xFD;

        public static DiskExtent GetUnderlyingVolumeData(Disk disk, PartitionTableEntry partitionTableEntry)
        {
            long partitionStartLBA = partitionTableEntry.FirstSectorLBA;
            long partitionSizeLBA = partitionTableEntry.SectorCountLBA;
            if (partitionTableEntry.SectorCountLBA > 0 && partitionTableEntry.PartitionType == LinuxRaidPartitionType)
            {
                byte[] possibleLvmSuperblockBytes = disk.ReadSector(partitionStartLBA + 8);
                uint possibleLvmSuperblockSignature = LittleEndianConverter.ToUInt32(possibleLvmSuperblockBytes, 0);
                if (possibleLvmSuperblockSignature == LvmSignature)
                {
                    ulong dataOffset = LittleEndianConverter.ToUInt64(possibleLvmSuperblockBytes, 0x80);
                    partitionSizeLBA = (uint)LittleEndianConverter.ToUInt64(possibleLvmSuperblockBytes, 0x88);

                    partitionStartLBA = partitionTableEntry.FirstSectorLBA + (long)dataOffset;
                    byte[] possibleLvmLabelBytes = disk.ReadSector(partitionStartLBA + 1);
                    ulong possibleLvmLabelSignature = LittleEndianConverter.ToUInt64(possibleLvmLabelBytes, 0);

                    if (possibleLvmLabelSignature == LvmLabelSignature)
                    {
                        // https://wiki.syslinux.org/wiki/index.php?title=Development/LVM_support
                        ulong actualDataOffset = LittleEndianConverter.ToUInt64(possibleLvmLabelBytes, 0x48);
                        long actualDataOffsetLBA = (long)actualDataOffset / 512;
                        //ulong actualDataSize = LittleEndianConverter.ToUInt64(possibleLvmLabelBytes, 0x40); // In bytes
                        partitionStartLBA += actualDataOffsetLBA;
                        partitionSizeLBA -= actualDataOffsetLBA;
                    }
                }
            }

            return new DiskExtent(disk, partitionStartLBA, partitionSizeLBA * disk.BytesPerSector);
        }
    }
}
