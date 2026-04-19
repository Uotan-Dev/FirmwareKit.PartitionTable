using System;
using System.IO;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Provides conservative repair utilities for partition tables.
    /// 提供保守的分区表修复工具。
    /// </summary>
    public static class PartitionTableRepair
    {
        /// <summary>
        /// Repairs GPT CRC-related metadata in-place on a writable stream.
        /// 在可写流上就地修复 GPT 的 CRC 相关元数据。
        /// </summary>
        /// <param name="stream">The target stream. / 目标流。</param>
        /// <param name="sectorSize">Preferred sector size, or <see langword="null" /> to auto-detect. / 首选扇区大小，传 <see langword="null" /> 时自动检测。</param>
        /// <returns>The repair outcome and performed actions. / 修复结果及执行动作。</returns>
        public static PartitionRepairResult RepairGptCrcInPlace(Stream stream, int? sectorSize = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek || !stream.CanWrite) throw new NotSupportedException("The stream must be readable, writable, and seekable.");

            var result = new PartitionRepairResult();
            var table = PartitionTableReader.FromStream(stream, mutable: true, sectorSize: sectorSize);
            if (table is not GptPartitionTable gpt)
            {
                result.Actions.Add("No GPT table was found; nothing repaired.");
                return result;
            }

            var report = PartitionTableDiagnostics.Analyze(gpt);
            gpt.WriteToStream(stream);
            result.Repaired = true;
            result.Actions.Add(report.IsHealthy
                ? "Refreshed GPT headers and entry arrays."
                : "Rewrote GPT headers and entry arrays to repair detected issues.");
            return result;
        }

        /// <summary>
        /// Repairs GPT CRC-related metadata in a file.
        /// 修复文件中的 GPT CRC 相关元数据。
        /// </summary>
        /// <param name="path">The image file path. / 镜像文件路径。</param>
        /// <param name="sectorSize">Preferred sector size, or <see langword="null" /> to auto-detect. / 首选扇区大小，传 <see langword="null" /> 时自动检测。</param>
        /// <returns>The repair outcome and performed actions. / 修复结果及执行动作。</returns>
        public static PartitionRepairResult RepairGptCrcInFile(string path, int? sectorSize = null)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return RepairGptCrcInPlace(stream, sectorSize);
        }
    }
}
