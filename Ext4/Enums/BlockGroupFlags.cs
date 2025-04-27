namespace VmdkZeroFree.Ext4
{
    public enum BlockGroupFlags : ushort
    {
        InodeNotInitialized = 0x01,       // EXT4_BG_INODE_UNINIT
        BlockBitmapNotInitialized = 0x02, // EXT4_BG_BLOCK_UNINIT
        InodeTableZeroed = 0x04,          // EXT4_BG_INODE_ZEROED
    }
}
