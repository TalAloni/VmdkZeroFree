/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using Utilities;

namespace VmdkZeroFree.Ext4
{
    public class BlockGroupDescriptor
    {
        public ulong BlockBitmapLocation;
        public ulong InodeBitmapLocation;
        public ulong InodeTableLocation;
        public ulong FreeBlocksCount;
        public ulong FreeInodesCount;
        public ulong UsedDirsCount;
        public BlockGroupFlags BlockGroupFlags;

        public BlockGroupDescriptor(byte[] buffer, int offset, int descriptorSize)
        {
            BlockBitmapLocation = LittleEndianConverter.ToUInt32(buffer, offset + 0x00);
            InodeBitmapLocation = LittleEndianConverter.ToUInt32(buffer, offset + 0x04);
            InodeTableLocation = LittleEndianConverter.ToUInt32(buffer, offset + 0x08);
            FreeBlocksCount = LittleEndianConverter.ToUInt16(buffer, offset + 0x0C);
            FreeInodesCount = LittleEndianConverter.ToUInt16(buffer, offset + 0x0E);
            UsedDirsCount = LittleEndianConverter.ToUInt16(buffer, offset + 0x10);
            BlockGroupFlags = (BlockGroupFlags)LittleEndianConverter.ToUInt32(buffer, offset + 0x12);

            if (descriptorSize > 32)
            {
                uint iblockBitmapLocationHigh = LittleEndianConverter.ToUInt32(buffer, offset + 0x20);
                BlockBitmapLocation |= (ulong)iblockBitmapLocationHigh << 32;
                uint inodeBitmapLocationHigh = LittleEndianConverter.ToUInt32(buffer, offset + 0x24);
                InodeBitmapLocation |= (ulong)inodeBitmapLocationHigh << 32;
                uint inodeTableLocationHigh = LittleEndianConverter.ToUInt32(buffer, offset + 0x28);
                InodeTableLocation |= (ulong)inodeTableLocationHigh << 32;

                uint freeBlocksCountHigh = LittleEndianConverter.ToUInt32(buffer, offset + 0x2C);
                FreeBlocksCount |= (ulong)freeBlocksCountHigh << 32;
                uint freeInodesCountHigh = LittleEndianConverter.ToUInt32(buffer, offset + 0x2E);
                FreeInodesCount |= (ulong)freeInodesCountHigh << 32;
                uint usedDirsCountHigh = LittleEndianConverter.ToUInt32(buffer, offset + 0x30);
                UsedDirsCount |= (ulong)usedDirsCountHigh << 32;
            }
        }
    }
}
