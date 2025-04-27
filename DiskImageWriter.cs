/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using DiskAccessLibrary;
using Utilities;

namespace VmdkZeroFree
{
    public class DiskImageWriter
    {
        private const int BufferSize = 2048;

        private DiskImage m_diskImage;
        private long m_position;
        private byte[] m_buffer;

        public DiskImageWriter(DiskImage diskImage)
        {
            m_diskImage = diskImage;
            m_position = diskImage.TotalSectors;
            m_buffer = new byte[0];
        }

        public void Write(byte[] data)
        {
            m_buffer = ByteUtils.Concatenate(m_buffer, data);
            m_position += data.Length / m_diskImage.BytesPerSector;
            if (m_buffer.Length >= BufferSize * m_diskImage.BytesPerSector)
            {
                Flush();
            }
        }

        public void Flush()
        {
            if (m_buffer.Length > 0)
            {
                long filePosition = m_diskImage.TotalSectors;
                m_diskImage.Extend(m_buffer.Length);
                m_diskImage.WriteSectors(filePosition, m_buffer);

                m_buffer = new byte[0];
            }
        }

        public long Position
        {
            get
            {
                return m_position;
            }
        }
    }
}
