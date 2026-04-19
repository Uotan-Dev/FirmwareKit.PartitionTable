using FirmwareKit.PartitionTable.Interfaces;
using System;
using System.IO;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Provides safety-focused write helpers.
    /// 提供面向安全性的写入辅助。
    /// </summary>
    public static class PartitionTableWriter
    {
        /// <summary>
        /// Writes a partition table to a file atomically using a temporary file and replace/move strategy.
        /// 使用临时文件与替换/移动策略原子写入分区表到文件。
        /// </summary>
        /// <param name="table">The partition table to write. / 待写入的分区表。</param>
        /// <param name="path">The destination file path. / 目标文件路径。</param>
        /// <param name="requireConfirmation">Whether a confirmation token is required. / 是否要求确认令牌。</param>
        /// <param name="confirmation">Confirmation token value. / 确认令牌内容。</param>
        public static void WriteToFileAtomic(IPartitionTable table, string path, bool requireConfirmation = true, string? confirmation = null, bool keepBackup = false)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (path == null) throw new ArgumentNullException(nameof(path));

            if (requireConfirmation)
            {
                string expected = "I_UNDERSTAND_PARTITION_WRITE";
                if (!string.Equals(confirmation, expected, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Partition write confirmation token is missing or invalid.");
                }
            }

            string directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp." + Guid.NewGuid().ToString("N"));
            try
            {
                using (var stream = File.Open(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    table.WriteToStream(stream);
                }

                if (File.Exists(path))
                {
                    string backupPath = path + ".bak";
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Replace(tempPath, path, backupPath);
                    if (!keepBackup)
                    {
                        File.Delete(backupPath);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
