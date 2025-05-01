/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using Utilities;

namespace VmdkZeroFree.Xfs
{
    public class AGFreeSpaceBlock
    {
        private const uint ValidSignature = 0x58414746; // XAGF

        public uint VersionNumber; // agf_versionnum
        public uint SequenceNumber; // agf_seqno
        public uint SizeInBlocks; // agf_length
        public uint FreespaceByBlockRootBlockNumber; // agf_roots[0]
        public uint FreespaceBySizeRootBlockNumber; // agf_roots[1]
        public uint FreespaceByBlockDepth; // agf_levels[0]
        public uint FreespaceBySizeDepth; // agf_levels[1]

        private AGFreeSpaceBlock(byte[] buffer)
        {
            VersionNumber = BigEndianConverter.ToUInt32(buffer, 0x04);
            SequenceNumber = BigEndianConverter.ToUInt32(buffer, 0x08);
            SizeInBlocks = BigEndianConverter.ToUInt32(buffer, 0x0C);
            FreespaceByBlockRootBlockNumber = BigEndianConverter.ToUInt32(buffer, 0x10);
            FreespaceBySizeRootBlockNumber = BigEndianConverter.ToUInt32(buffer, 0x14);
            FreespaceByBlockDepth = BigEndianConverter.ToUInt32(buffer, 0x1C);
            FreespaceBySizeDepth = BigEndianConverter.ToUInt32(buffer, 0x20);
        }

        public static AGFreeSpaceBlock ReadAGFreeSpaceBlock(byte[] buffer)
        {
            uint magic = BigEndianConverter.ToUInt32(buffer, 0x00);
            if (magic == ValidSignature)
            {
                return new AGFreeSpaceBlock(buffer);
            }
            else
            {
                return null;
            }
        }
    }
}
