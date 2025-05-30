﻿/* Copyright (C) 2012-2020 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;

namespace Utilities
{
    public static class ByteUtils
    {
        public static bool IsAllZeros(byte[] array, int offset, int count)
        {
            for (int index = 0; index < count; index++)
            {
                if (array[offset + index] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        public static byte[] Concatenate(byte[] a, byte[] b)
        {
            byte[] result = new byte[a.Length + b.Length];
            Array.Copy(a, 0, result, 0, a.Length);
            Array.Copy(b, 0, result, a.Length, b.Length);
            return result;
        }
    }
}
