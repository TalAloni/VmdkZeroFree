/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using System;
using Utilities;

namespace VmdkZeroFree
{
    public class DiskImageReader
    {
        private const int BufferSize = 2048;

        private DiskImage m_diskImage;
        private long m_position;
        private byte[] m_buffer;
        private long m_bufferStartPosition;

        public DiskImageReader(DiskImage diskImage)
        {
            m_diskImage = diskImage;
            m_position = 0;
            m_buffer = new byte[0];
        }

        public byte[] ReadSector()
        {
            return ReadSectors(1);
        }

        public byte[] ReadSectors(int count)
        {
            long bufferEndPosition = m_bufferStartPosition + m_buffer.Length / m_diskImage.BytesPerSector - 1;
            if (m_position < m_bufferStartPosition)
            {
                throw new InvalidOperationException("Read must be sequential");
            }
            else if (m_position > bufferEndPosition || m_buffer.Length == 0)
            {
                m_buffer = ReadUnbuffered(m_position);
                m_bufferStartPosition = m_position;
            }
            else if (m_position + count > bufferEndPosition)
            {
                // Fill buffer with missing sectors
                int startOffsetInBuffer = (int)(m_position - m_bufferStartPosition) * m_diskImage.BytesPerSector;
                byte[] existingData = ByteReader.ReadBytes(m_buffer, startOffsetInBuffer, m_buffer.Length - startOffsetInBuffer);
                byte[] data = ReadUnbuffered(bufferEndPosition + 1);
                m_buffer = ByteUtils.Concatenate(existingData, data);

                m_bufferStartPosition = m_position;
            }

            int bufferStartPosition = (int)(m_position - m_bufferStartPosition) * m_diskImage.BytesPerSector;

            byte[] result = ByteReader.ReadBytes(m_buffer, bufferStartPosition, count * m_diskImage.BytesPerSector);
            m_position += count;
            return result;
        }

        private byte[] ReadUnbuffered(long startPosition)
        {
            long sectorsRemaining = m_diskImage.TotalSectors - startPosition;
            int sectorsToRead = (int)Math.Min(BufferSize, sectorsRemaining);
            return m_diskImage.ReadSectors(startPosition, sectorsToRead);
        }

        public long Position
        {
            get
            {
                return m_position;
            }
        }

        public long TotalSectors
        {
            get
            {
                return m_diskImage.TotalSectors;
            }
        }
    }
}
