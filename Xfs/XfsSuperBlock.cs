/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Text;
using Utilities;
using static DiskAccessLibrary.SecurityUtils;

namespace VmdkZeroFree.Xfs
{
    public class XfsSuperBlock
    {
        private const uint ValidSignature = 0x58465342; // XFSB
        private const int NameLength = 12;

        public uint BlockSize; // sb_blocksize, in bytes
        public ulong DataBlocks; // sb_dblocks
        public ulong RealtimeBlocks; // sb_rblocks
        public ulong RealtimeExtents; // sb_rextents
        public Guid Uuid; // sb_uuid
        // sb_logstart
        // sb_rootino
        // sb_rbmino
        // sb_rsumino
        public uint RealtimeExtentSize; // sb_rextsize
        public uint AGBlocks; // sb_agblocks, size of each AG in blocks
        public uint AGCount; // sb_agcount, number of AGs in the filesystem 
        // sb_rbmblocks
        public uint LogBlocks; // sb_logblocks
        public ushort VersionNum; // sb_versionnum
        public ushort SectorSize; // sb_sectsize
        public ushort INodeSize; // sb_inodesize, size of the inode in bytes
        public ushort INodesPerBlock; // sb_inopblock
        public string Name; // sb_fname

        private XfsSuperBlock(byte[] buffer)
        {
            BlockSize = BigEndianConverter.ToUInt32(buffer, 0x04);
            DataBlocks = BigEndianConverter.ToUInt32(buffer, 0x08);
            RealtimeBlocks = BigEndianConverter.ToUInt64(buffer, 0x10);
            RealtimeExtents = BigEndianConverter.ToUInt64(buffer, 0x18);
            Uuid = BigEndianConverter.ToGuid(buffer, 0x20);
            RealtimeExtentSize = BigEndianConverter.ToUInt32(buffer, 0x50);
            AGBlocks = BigEndianConverter.ToUInt32(buffer, 0x54);
            AGCount = BigEndianConverter.ToUInt32(buffer, 0x58);
            LogBlocks = BigEndianConverter.ToUInt32(buffer, 0x60);
            VersionNum = BigEndianConverter.ToUInt16(buffer, 0x64);
            SectorSize = BigEndianConverter.ToUInt16(buffer, 0x66);
            INodeSize = BigEndianConverter.ToUInt16(buffer, 0x68);
            INodesPerBlock = BigEndianConverter.ToUInt16(buffer, 0x6A);
            Name = Encoding.ASCII.GetString(buffer, 0x6C, NameLength).TrimEnd('\0');
        }

        public bool IsV5 => (byte)(VersionNum & 0x0F) == 0x05;

        public static XfsSuperBlock ReadXfsSuperBlock(byte[] buffer)
        {
            uint magic = BigEndianConverter.ToUInt32(buffer, 0x00);
            if (magic == ValidSignature)
            {
                return new XfsSuperBlock(buffer);
            }
            else
            {
                return null;
            }
        }
    }
}
