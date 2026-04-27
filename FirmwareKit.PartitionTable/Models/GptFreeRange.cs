namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// Represents a free (unallocated) range in a GPT partition table.
    /// 表示 GPT 分区表中的空闲（未分配）范围。
    /// </summary>
    public sealed class GptFreeRange
    {
        /// <summary>
        /// Gets or sets the first LBA of the free range.
        /// 获取或设置空闲范围的起始 LBA。
        /// </summary>
        public ulong FirstLba { get; set; }

        /// <summary>
        /// Gets or sets the last LBA of the free range.
        /// 获取或设置空闲范围的结束 LBA。
        /// </summary>
        public ulong LastLba { get; set; }

        /// <summary>
        /// Gets or sets the number of sectors in the free range.
        /// 获取或设置空闲范围的扇区数。
        /// </summary>
        public ulong SectorCount { get; set; }
    }
}
