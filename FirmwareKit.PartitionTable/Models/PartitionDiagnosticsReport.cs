using System.Collections.Generic;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Diagnostic report for a partition table.
    /// 分区表诊断报告。
    /// </summary>
    public sealed class PartitionDiagnosticsReport
    {
        /// <summary>
        /// Gets or sets the partition table kind that was analyzed.
        /// 获取或设置被分析的分区表类型。
        /// </summary>
        public PartitionTableType Type { get; set; }

        /// <summary>
        /// Gets the collected diagnostic issues.
        /// 获取收集到的诊断问题列表。
        /// </summary>
        public List<PartitionDiagnosticIssue> Issues { get; } = new List<PartitionDiagnosticIssue>();

        /// <summary>
        /// Gets a value indicating whether no error-level issue exists.
        /// 获取是否不存在错误级问题。
        /// </summary>
        public bool IsHealthy
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Severity == PartitionIssueSeverity.Error)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
