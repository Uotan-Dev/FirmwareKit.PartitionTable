namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// Represents a free (unallocated) range in an MBR partition table.
    /// 表示 MBR 分区表中的空闲（未分配）范围。
    /// </summary>
    public sealed class MbrFreeRange
    {
        /// <summary>
        /// Gets or sets the first LBA of the free range.
        /// 获取或设置空闲范围的起始 LBA。
        /// </summary>
        public uint FirstLba { get; set; }

        /// <summary>
        /// Gets or sets the number of sectors in the free range.
        /// 获取或设置空闲范围的扇区数。
        /// </summary>
        public uint SectorCount { get; set; }
    }
}
