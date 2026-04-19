namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// The kind of table difference.
    /// 表差异类型。
    /// </summary>
    public enum PartitionDiffKind
    {
        /// <summary>
        /// A partition entry was added.
        /// 新增了一个分区项。
        /// </summary>
        Added,
        /// <summary>
        /// A partition entry was removed.
        /// 移除了一个分区项。
        /// </summary>
        Removed,
        /// <summary>
        /// A partition entry was modified.
        /// 修改了一个分区项。
        /// </summary>
        Modified
    }
}