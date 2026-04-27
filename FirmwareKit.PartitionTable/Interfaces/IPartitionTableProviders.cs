using FirmwareKit.PartitionTable.Models;
using System.IO;

namespace FirmwareKit.PartitionTable.Interfaces
{
    /// <summary>
    /// Interface for pluggable partition table writers.
    /// 可插拔的分区表写入器接口。
    /// </summary>
    public interface IPartitionTableWriter
    {
        /// <summary>
        /// Gets the partition table type this writer supports.
        /// 获取此写入器支持的分区表类型。
        /// </summary>
        PartitionTableType SupportedType { get; }

        /// <summary>
        /// Writes a partition table to a stream.
        /// 将分区表写入流。
        /// </summary>
        /// <param name="table">The partition table to write.</param>
        /// <param name="stream">The destination stream.</param>
        void WriteToStream(IPartitionTable table, Stream stream);
    }

    /// <summary>
    /// Interface for pluggable partition table diagnostics providers.
    /// 可插拔的分区表诊断提供器接口。
    /// </summary>
    public interface IPartitionTableDiagnosticsProvider
    {
        /// <summary>
        /// Gets the partition table type this diagnostics provider supports.
        /// 获取此诊断提供器支持的分区表类型。
        /// </summary>
        PartitionTableType SupportedType { get; }

        /// <summary>
        /// Analyzes a partition table and returns diagnostic issues.
        /// 分析分区表并返回诊断问题。
        /// </summary>
        /// <param name="table">The partition table to analyze.</param>
        /// <returns>The diagnostic report.</returns>
        PartitionDiagnosticsReport Analyze(IPartitionTable table);
    }

    /// <summary>
    /// Interface for pluggable partition table repair providers.
    /// 可插拔的分区表修复提供器接口。
    /// </summary>
    public interface IPartitionTableRepairProvider
    {
        /// <summary>
        /// Gets the partition table type this repair provider supports.
        /// 获取此修复提供器支持的分区表类型。
        /// </summary>
        PartitionTableType SupportedType { get; }

        /// <summary>
        /// Attempts to repair a partition table in-place on the given stream.
        /// 尝试在给定流上就地修复分区表。
        /// </summary>
        /// <param name="stream">The target stream positioned at the beginning of the disk image.</param>
        /// <param name="sectorSize">The sector size in bytes, or null for auto-detection.</param>
        /// <returns>The repair result.</returns>
        PartitionRepairResult RepairInPlace(Stream stream, int? sectorSize = null);
    }
}
