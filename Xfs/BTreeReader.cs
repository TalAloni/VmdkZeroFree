/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using System.Collections.Generic;
using System.IO;
using Utilities;

namespace VmdkZeroFree.Xfs
{
    public static class BTreeReader
    {
        private const uint ReadFreeSpaceByBlockV4Signature = 0x41425442; // ABTB
        private const uint FreeSpaceBySizeV4Signature = 0x41425443; // ABTC
        private const uint ReadFreeSpaceByBlockV5Signature = 0x41423342; // AB3B
        private const uint FreeSpaceBySizeV5Signature = 0x41423343; // AB3C

        public static List<KeyValuePair<uint, uint>> ReadFreeSpaceByBlockBTreeEntries(DiskExtent volume, long allocationGroupStartSector, uint rootNodeBlockIndex, XfsSuperBlock superBlock)
        {
            uint expectedSignature = superBlock.IsV5 ? ReadFreeSpaceByBlockV5Signature : ReadFreeSpaceByBlockV4Signature;
            return ReadBTreeEntries(volume, allocationGroupStartSector, rootNodeBlockIndex, superBlock, expectedSignature);
        }

        public static List<KeyValuePair<uint, uint>> ReadFreeSpaceBySizeBTreeEntries(DiskExtent volume, long allocationGroupStartSector, uint rootNodeBlockIndex, XfsSuperBlock superBlock)
        {
            uint expectedSignature = superBlock.IsV5 ? FreeSpaceBySizeV5Signature : FreeSpaceBySizeV4Signature;
            return ReadBTreeEntries(volume, allocationGroupStartSector, rootNodeBlockIndex, superBlock, expectedSignature);
        }

        private static List<KeyValuePair<uint, uint>> ReadBTreeEntries(DiskExtent volume, long allocationGroupStartSector, uint rootNodeBlockIndex, XfsSuperBlock superBlock, uint expectedSignature)
        {
            int sectorsPerBlock = (int)(superBlock.BlockSize / volume.BytesPerSector);
            byte[] nodeBytes = volume.ReadSectors(allocationGroupStartSector + rootNodeBlockIndex * sectorsPerBlock, sectorsPerBlock);
            ShortFormatBTreeHeader header = ShortFormatBTreeHeader.ReadShortFormatBTreeHeader(nodeBytes, superBlock.IsV5, expectedSignature);

            int nodePointersOffset = GetNodePointersOffset(superBlock, header);
            while (header.Level > 0)
            {
                List<uint> pointers = ReadNodePointers(header, nodeBytes, nodePointersOffset);
                uint nodeBlockIndex = pointers[0];
                nodeBytes = volume.ReadSectors(allocationGroupStartSector + nodeBlockIndex * sectorsPerBlock, sectorsPerBlock);
                header = ShortFormatBTreeHeader.ReadShortFormatBTreeHeader(nodeBytes, superBlock.IsV5, expectedSignature);
            }

            // We have found the leftmost leaf node and we will scan all leaf nodes to the right
            if (header.LeftSibling != 0xFFFFFFFF)
            {
                throw new InvalidDataException("XFS file system is corrupt");
            }

            List<KeyValuePair<uint, uint>> result = new List<KeyValuePair<uint, uint>>();
            List<KeyValuePair<uint, uint>> leafEntries = ReadLeafEntries(header, nodeBytes);
            result.AddRange(leafEntries);

            while (header.RightSibling != 0xFFFFFFFF)
            {
                nodeBytes = volume.ReadSectors(allocationGroupStartSector + header.RightSibling * sectorsPerBlock, sectorsPerBlock);
                header = ShortFormatBTreeHeader.ReadShortFormatBTreeHeader(nodeBytes, superBlock.IsV5, expectedSignature);
                leafEntries = ReadLeafEntries(header, nodeBytes);
                result.AddRange(leafEntries);
            }

            return result;
        }

        private static int GetNodePointersOffset(XfsSuperBlock superBlock, ShortFormatBTreeHeader header)
        {
            int bytesAvailableWithoutHeader = (int)superBlock.BlockSize - header.Length;
            int keyLength = 8;
            int ptrLength = 4;
            int maxNumberOfRecords = bytesAvailableWithoutHeader / (keyLength + ptrLength);
            return header.Length + maxNumberOfRecords * keyLength;
        }

        private static List<uint> ReadNodePointers(ShortFormatBTreeHeader header, byte[] nodeBytes, int nodePointersOffset)
        {
            List<uint> result = new List<uint>();

            int offsetInBuffer = nodePointersOffset;
            for (int index = 0; index < header.NumberOfRecords; index++)
            {
                uint ptr = BigEndianConverter.ToUInt32(nodeBytes, offsetInBuffer);
                result.Add(ptr);
                offsetInBuffer += 4;
            }

            return result;
        }
        
        private static List<KeyValuePair<uint, uint>> ReadLeafEntries(ShortFormatBTreeHeader header, byte[] nodeBytes)
        {
            List<KeyValuePair<uint, uint>> result = new List<KeyValuePair<uint, uint>>();

            int offsetInBuffer = header.Length;
            for (int index = 0; index < header.NumberOfRecords; index++)
            {
                uint startBlock = BigEndianConverter.ToUInt32(nodeBytes, offsetInBuffer);
                uint blockCount = BigEndianConverter.ToUInt32(nodeBytes, offsetInBuffer + 4);
                result.Add(new KeyValuePair<uint, uint>(startBlock, blockCount));
                offsetInBuffer += 8;
            }

            return result;
        }
    }
}
