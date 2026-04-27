namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// Represents a free (unallocated) range in an Amlogic EPT partition table.
    /// 表示 Amlogic EPT 分区表中的空闲（未分配）范围。
    /// </summary>
    public sealed class AmlogicFreeRange
    {
        /// <summary>
        /// Gets or sets the byte offset of the free range.
        /// 获取或设置空闲范围的字节偏移。
        /// </summary>
        public ulong Offset { get; set; }

        /// <summary>
        /// Gets or sets the size in bytes of the free range.
        /// 获取或设置空闲范围的大小（字节）。
        /// </summary>
        public ulong Size { get; set; }
    }
}
