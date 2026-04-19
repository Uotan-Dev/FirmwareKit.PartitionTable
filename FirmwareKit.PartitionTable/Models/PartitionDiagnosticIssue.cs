namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// One diagnostic issue found during partition table validation.
    /// 分区表校验发现的单条问题。
    /// </summary>
    public sealed class PartitionDiagnosticIssue
    {
        /// <summary>
        /// Gets or sets the machine-readable issue code.
        /// 获取或设置机器可读的问题代码。
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the human-readable issue message.
        /// 获取或设置人类可读的问题描述。
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the issue severity level.
        /// 获取或设置问题严重级别。
        /// </summary>
        public PartitionIssueSeverity Severity { get; set; } = PartitionIssueSeverity.Error;

        /// <summary>
        /// Gets or sets whether this issue can be repaired automatically.
        /// 获取或设置该问题是否可自动修复。
        /// </summary>
        public bool CanAutoRepair { get; set; }
    }
}
