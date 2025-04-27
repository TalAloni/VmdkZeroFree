/* Copyright (C) 2012-2020 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;

namespace Utilities
{
    public static class LittleEndianWriter
    {
        public static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            byte[] bytes = LittleEndianConverter.GetBytes(value);
            Array.Copy(bytes, 0, buffer, offset, bytes.Length);
        }

        public static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            byte[] bytes = LittleEndianConverter.GetBytes(value);
            Array.Copy(bytes, 0, buffer, offset, bytes.Length);
        }
    }
}
