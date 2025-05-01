/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.IO;
using Utilities;

namespace VmdkZeroFree.Xfs
{
    /// <summary>
    /// xfs_btree_sblock
    /// </summary>
    public class ShortFormatBTreeHeader
    {
        private const int LengthV4 = 0x10;
        private const int LengthV5 = 0x38;

        private bool m_isV5;

        public uint Magic; // bb_magic
        public ushort Level; // bb_level
        public ushort NumberOfRecords; // bb_numrecs
        public uint LeftSibling; // bb_leftsib
        public uint RightSibling; // bb_rightsib
        // v5:
        public ulong BlockNumber; // bb_blkno
        public ulong Lsn; // bb_lsn
        public Guid Uuid; // bb_uuid
        public uint Owner; // bb_owner
        public uint Crc; // bb_crc

        private ShortFormatBTreeHeader(byte[] buffer, bool isV5)
        {
            m_isV5 = isV5;

            Magic = BigEndianConverter.ToUInt32(buffer, 0x00);
            Level = BigEndianConverter.ToUInt16(buffer, 0x04);
            NumberOfRecords = BigEndianConverter.ToUInt16(buffer, 0x06);
            LeftSibling = BigEndianConverter.ToUInt32(buffer, 0x08);
            RightSibling = BigEndianConverter.ToUInt32(buffer, 0x0C);

            if (isV5)
            {
                BlockNumber = BigEndianConverter.ToUInt64(buffer, 0x10);
                Lsn = BigEndianConverter.ToUInt64(buffer, 0x18);
                Uuid = BigEndianConverter.ToGuid(buffer, 0x20);
                Owner = BigEndianConverter.ToUInt32(buffer, 0x30);
                Crc = BigEndianConverter.ToUInt32(buffer, 0x34);
            }
        }

        public int Length
        {
            get
            {
                return m_isV5 ? LengthV5 : LengthV4;
            }
        }

        public static ShortFormatBTreeHeader ReadShortFormatBTreeHeader(byte[] buffer, bool isV5, uint expectedSignature)
        {
            uint magic = BigEndianConverter.ToUInt32(buffer, 0x00);
            if (magic != expectedSignature)
            {
                throw new InvalidDataException($"Block signature 0x{magic.ToString("X")} does not match expected signature 0x{expectedSignature.ToString("X")}");
            }

            return new ShortFormatBTreeHeader(buffer, isV5);
        }
    }
}
