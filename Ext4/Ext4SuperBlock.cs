/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using Utilities;

namespace VmdkZeroFree.Ext4
{
    // https://www.kernel.org/doc/html/latest/filesystems/ext4/globals.html#super-block
    // https://archive.kernel.org/oldwiki/ext4.wiki.kernel.org/index.php/Ext4_Disk_Layout.html
    public class Ext4SuperBlock
    {
        private const int ValidSignature = 0xEF53;
        private const int LegacyGroupDescriptorSize = 32;

        public uint InodesCount;
        public uint BlocksCount;
        public uint RBlocksCount;
        public uint FreeBlocksCount;
        public uint FreeInodesCount;
        public uint FirstDataBlock;
        public uint LogBlockSize;
        public uint LogClusterSize;
        public uint BlocksPerGroup;
        public IncompatibleFeatureFlags FeatureIncompat;
        public ReadOnlyCompatibleFeatureFlags FeatureReadOnlyCompat;

        public ushort DescSize;

        private Ext4SuperBlock(byte[] buffer)
        {
            InodesCount = LittleEndianConverter.ToUInt32(buffer, 0x00);
            BlocksCount = LittleEndianConverter.ToUInt32(buffer, 0x04);
            RBlocksCount = LittleEndianConverter.ToUInt32(buffer, 0x08);
            FreeBlocksCount = LittleEndianConverter.ToUInt32(buffer, 0x0C);
            FreeInodesCount = LittleEndianConverter.ToUInt32(buffer, 0x10);
            FirstDataBlock = LittleEndianConverter.ToUInt32(buffer, 0x14);
            LogBlockSize = LittleEndianConverter.ToUInt32(buffer, 0x18);
            LogClusterSize = LittleEndianConverter.ToUInt32(buffer, 0x1C);
            BlocksPerGroup = LittleEndianConverter.ToUInt32(buffer, 0x20);
            FeatureIncompat = (IncompatibleFeatureFlags)LittleEndianConverter.ToUInt32(buffer, 0x60);
            FeatureReadOnlyCompat = (ReadOnlyCompatibleFeatureFlags)LittleEndianConverter.ToUInt32(buffer, 0x64);

            DescSize = LittleEndianConverter.ToUInt16(buffer, 0xFE);
        }

        public int BlockSize => 1024 << (int)LogBlockSize;

        public int ClusterSize => 1024 << (int)LogClusterSize;

        public bool EnableFileSystemOver4GibiBlocks => (FeatureIncompat & IncompatibleFeatureFlags.EnableFileSystemOver4GibiBlocks) > 0;

        public bool SparseSuperBlocks => (FeatureReadOnlyCompat & ReadOnlyCompatibleFeatureFlags.SparseSuperBlocks) > 0;

        public int GroupDescriptorSize => EnableFileSystemOver4GibiBlocks ? DescSize : LegacyGroupDescriptorSize;

        public static Ext4SuperBlock ReadExt4SuperBlock(byte[] buffer)
        {
            ushort magic = LittleEndianConverter.ToUInt16(buffer, 0x38);
            if (magic == ValidSignature)
            {
                return new Ext4SuperBlock(buffer);
            }
            else
            {
                return null;
            }
        }
    }
}
