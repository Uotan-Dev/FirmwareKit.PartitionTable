using System.Collections.Generic;

namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// Result of a repair operation.
    /// 修复操作结果。
    /// </summary>
    public sealed class PartitionRepairResult
    {
        /// <summary>
        /// Gets or sets whether a repair was actually performed.
        /// 获取或设置是否实际执行了修复。
        /// </summary>
        public bool Repaired { get; set; }

        /// <summary>
        /// Gets the textual actions performed during repair.
        /// 获取修复过程中执行动作的文本列表。
        /// </summary>
        public List<string> Actions { get; } = new List<string>();
    }
}
