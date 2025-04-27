using System;

namespace VmdkZeroFree.Ext4
{
    [Flags]
    public enum IncompatibleFeatureFlags : uint
    {
        EnableFileSystemOver4GibiBlocks = 0x80, // 2^32, INCOMPAT_64BIT
    }
}
