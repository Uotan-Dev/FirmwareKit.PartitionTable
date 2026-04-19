using System.Collections.Generic;

namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// Dry-run write plan for a partition table.
    /// 分区表干跑写入计划。
    /// </summary>
    public sealed class PartitionWritePlan
    {
        /// <summary>
        /// Gets or sets the partition table kind for this write plan.
        /// 获取或设置该写入计划对应的分区表类型。
        /// </summary>
        public PartitionTableType Type { get; set; }

        /// <summary>
        /// Gets the ordered write ranges that would be written.
        /// 获取将被写入的有序区间列表。
        /// </summary>
        public List<PartitionWriteRange> Ranges { get; } = new List<PartitionWriteRange>();
    }
}
