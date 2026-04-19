using System.Collections.Generic;

namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// A structured diff between two partition tables.
    /// 两张分区表之间的结构化差异。
    /// </summary>
    public sealed class PartitionTableDiff
    {
        /// <summary>
        /// Gets or sets the partition table type.
        /// 获取或设置分区表类型。
        /// </summary>
        public PartitionTableType Type { get; set; }

        /// <summary>
        /// Gets the collected differences.
        /// 获取收集到的差异项。
        /// </summary>
        public List<PartitionDiffEntry> Entries { get; } = new List<PartitionDiffEntry>();

        /// <summary>
        /// Gets a value indicating whether any differences were found.
        /// 获取是否存在差异。
        /// </summary>
        public bool HasDifferences => Entries.Count > 0;
    }
}