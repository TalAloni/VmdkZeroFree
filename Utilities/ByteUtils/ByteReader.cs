/* Copyright (C) 2012-2020 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;

namespace Utilities
{
    public static class ByteReader
    {
        public static byte[] ReadBytes(byte[] buffer, int offset, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(buffer, offset, result, 0, length);
            return result;
        }
    }
}
