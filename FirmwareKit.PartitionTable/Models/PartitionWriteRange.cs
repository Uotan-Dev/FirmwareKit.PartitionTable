namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// One write range in a dry-run write plan.
    /// 干跑写入计划中的单个写入区间。
    /// </summary>
    public sealed class PartitionWriteRange
    {
        /// <summary>
        /// Gets or sets the byte offset where writing starts.
        /// 获取或设置写入起始的字节偏移。
        /// </summary>
        public long Offset { get; set; }

        /// <summary>
        /// Gets or sets the number of bytes to write.
        /// 获取或设置要写入的字节数。
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// Gets or sets a human-readable description of this write range.
        /// 获取或设置该写入区间的人类可读描述。
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }
}
