namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// The severity level of a partition table diagnostic issue.
    /// 分区表诊断问题的严重级别。
    /// </summary>
    public enum PartitionIssueSeverity
    {
        /// <summary>
        /// Informational issue that does not indicate a fault.
        /// 信息级问题，不表示故障。
        /// </summary>
        Info,

        /// <summary>
        /// Warning issue that may require attention.
        /// 警告级问题，可能需要关注。
        /// </summary>
        Warning,

        /// <summary>
        /// Error issue that indicates an invalid or unhealthy state.
        /// 错误级问题，表示状态无效或不健康。
        /// </summary>
        Error
    }
}
