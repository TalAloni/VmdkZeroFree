/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using System;

namespace VmdkZeroFree
{
    public class TrimmableDisk : Disk
    {
        private Disk m_underlyingDisk;
        private int m_trimmableBlockSizeInSectors;
        private byte[] m_bitmap; // Bit is set if block can be trimmed

        public TrimmableDisk(Disk underlyingDisk, int trimmableBlockSizeInSectors)
        {
            m_underlyingDisk = underlyingDisk;
            m_trimmableBlockSizeInSectors = trimmableBlockSizeInSectors;
            if (m_underlyingDisk.TotalSectors % trimmableBlockSizeInSectors > 0)
            {
                throw new ArgumentException($"TotalSectors must be divisable by {nameof(trimmableBlockSizeInSectors)}", nameof(trimmableBlockSizeInSectors));
            }

            int numberOfBlocks = (int)(m_underlyingDisk.TotalSectors / trimmableBlockSizeInSectors);
            int bitmapLength = (int)Math.Ceiling((double)numberOfBlocks / 8);
            m_bitmap = new byte[bitmapLength];
        }

        public override byte[] ReadSectors(long sectorIndex, int sectorCount)
        {
            byte[] data = m_underlyingDisk.ReadSectors(sectorIndex, sectorCount);
            return ApplyTrim(data, sectorIndex, sectorCount);
        }

        internal byte[] ApplyTrim(byte[] data, long sectorIndex, int sectorCount)
        {
            int blockCount = sectorCount / m_trimmableBlockSizeInSectors;
            int firstBlockIndex = (int)(sectorIndex / m_trimmableBlockSizeInSectors);
            int blockSizeInBytes = m_trimmableBlockSizeInSectors * m_underlyingDisk.BytesPerSector;
            byte[] emptyBlock = null;
            for (int offset = 0; offset < blockCount; offset++)
            {
                if (!IsBitClear(m_bitmap, firstBlockIndex + offset))
                {
                    if (sectorIndex % m_trimmableBlockSizeInSectors > 0)
                    {
                        throw new ArgumentException($"{nameof(sectorIndex)} must align to block boundary", nameof(sectorIndex));
                    }

                    if (sectorCount % m_trimmableBlockSizeInSectors > 0)
                    {
                        throw new ArgumentException($"{nameof(sectorIndex)} must be multiple of block size", nameof(sectorIndex));
                    }

                    if (emptyBlock == null)
                    {
                        emptyBlock = new byte[blockSizeInBytes];
                    }

                    Array.Copy(emptyBlock, 0, data, offset * blockSizeInBytes, emptyBlock.Length);
                }
            }
            return data;
        }

        public void TrimBlocks(long sectorIndex, int sectorCount)
        {
            if (sectorIndex % m_trimmableBlockSizeInSectors > 0)
            {
                throw new ArgumentException($"{nameof(sectorIndex)} must align to block boundary", nameof(sectorIndex));
            }

            if (sectorCount % m_trimmableBlockSizeInSectors > 0)
            {
                throw new ArgumentException($"{nameof(sectorIndex)} must be multiple of block size", nameof(sectorIndex));
            }

            int blockCount = sectorCount / m_trimmableBlockSizeInSectors;
            int firstBlockIndex = (int)(sectorIndex / m_trimmableBlockSizeInSectors);
            for (int offset = 0; offset < blockCount; offset++)
            {
                SetBit(m_bitmap, firstBlockIndex + offset);
            }
        }

        public override void WriteSectors(long sectorIndex, byte[] data)
        {
            throw new NotSupportedException();
        }

        public override int BytesPerSector => m_underlyingDisk.BytesPerSector;

        public override long Size => m_underlyingDisk.Size;

        private static bool IsBitClear(byte[] bitmap, int bitOffsetInBitmap)
        {
            int byteOffset = bitOffsetInBitmap / 8;
            int bitOffsetInByte = bitOffsetInBitmap % 8;
            bool isInUse = ((bitmap[byteOffset] >> bitOffsetInByte) & 0x01) != 0;
            return !isInUse;
        }

        private static void SetBit(byte[] bitmap, int bitOffsetInBitmap)
        {
            int byteOffset = bitOffsetInBitmap / 8;
            int bitOffsetInByte = bitOffsetInBitmap % 8;
            bitmap[byteOffset] |= (byte)(0x01 << bitOffsetInByte);
        }
    }
}
