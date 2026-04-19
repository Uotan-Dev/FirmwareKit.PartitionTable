namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// One detected difference between two partition tables.
    /// 两张分区表之间的一条差异。
    /// </summary>
    public sealed class PartitionDiffEntry
    {
        /// <summary>
        /// Gets or sets the zero-based entry index.
        /// 获取或设置零基项索引。
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the difference kind.
        /// 获取或设置差异类型。
        /// </summary>
        public PartitionDiffKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the human-readable description.
        /// 获取或设置人类可读描述。
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the left-side summary.
        /// 获取或设置左侧摘要。
        /// </summary>
        public string? Left { get; set; }

        /// <summary>
        /// Gets or sets the right-side summary.
        /// 获取或设置右侧摘要。
        /// </summary>
        public string? Right { get; set; }
    }
}