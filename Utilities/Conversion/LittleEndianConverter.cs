/* Copyright (C) 2012-2020 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;

namespace Utilities
{
    public static class LittleEndianConverter
    {
        public static ushort ToUInt16(byte[] buffer, int offset)
        {
            return (ushort)((buffer[offset + 1] << 8) | (buffer[offset + 0] << 0));
        }

        public static short ToInt16(byte[] buffer, int offset)
        {
            return (short)ToUInt16(buffer, offset);
        }

        public static uint ToUInt32(byte[] buffer, int offset)
        {
            return (uint)((buffer[offset + 3] << 24) | (buffer[offset + 2] << 16)
                | (buffer[offset + 1] << 8) | (buffer[offset + 0] << 0));
        }

        public static int ToInt32(byte[] buffer, int offset)
        {
            return (int)ToUInt32(buffer, offset);
        }

        public static ulong ToUInt64(byte[] buffer, int offset)
        {
            return (((ulong)ToUInt32(buffer, offset + 4)) << 32) | ToUInt32(buffer, offset + 0);
        }

        public static long ToInt64(byte[] buffer, int offset)
        {
            return (long)ToUInt64(buffer, offset);
        }

        public static byte[] GetBytes(ushort value)
        {
            byte[] result = new byte[2];
            result[0] = (byte)((value >> 0) & 0xFF);
            result[1] = (byte)((value >> 8) & 0xFF);
            return result;
        }

        public static byte[] GetBytes(short value)
        {
            return GetBytes((ushort)value);
        }

        public static byte[] GetBytes(uint value)
        {
            byte[] result = new byte[4];
            result[0] = (byte)((value >> 0) & 0xFF);
            result[1] = (byte)((value >> 8) & 0xFF);
            result[2] = (byte)((value >> 16) & 0xFF);
            result[3] = (byte)((value >> 24) & 0xFF);

            return result;
        }

        public static byte[] GetBytes(int value)
        {
            return GetBytes((uint)value);
        }

        public static byte[] GetBytes(ulong value)
        {
            byte[] result = new byte[8];
            Array.Copy(GetBytes((uint)(value & 0xFFFFFFFF)), 0, result, 0, 4);
            Array.Copy(GetBytes((uint)(value >> 32)), 0, result, 4, 4);

            return result;
        }

        public static byte[] GetBytes(long value)
        {
            return GetBytes((ulong)value);
        }
    }
}
