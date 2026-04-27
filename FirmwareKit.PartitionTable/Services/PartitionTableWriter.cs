using FirmwareKit.PartitionTable.Exceptions;
using FirmwareKit.PartitionTable.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
        /// <param name="keepBackup">Whether to preserve the existing file as a backup. / 是否保留现有文件作为备份。</param>
        public static void WriteToFileAtomic(IPartitionTable table, string path, bool requireConfirmation = true, string? confirmation = null, bool keepBackup = false)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (path == null) throw new ArgumentNullException(nameof(path));

            ValidateConfirmation(requireConfirmation, confirmation);

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

        /// <summary>
        /// Asynchronously writes a partition table to a file atomically using a temporary file and replace/move strategy.
        /// 使用临时文件与替换/移动策略异步原子写入分区表到文件。
        /// </summary>
        /// <param name="table">The partition table to write. / 待写入的分区表。</param>
        /// <param name="path">The destination file path. / 目标文件路径。</param>
        /// <param name="requireConfirmation">Whether a confirmation token is required. / 是否要求确认令牌。</param>
        /// <param name="confirmation">Confirmation token value. / 确认令牌内容。</param>
        /// <param name="keepBackup">Whether to preserve the existing file as a backup. / 是否保留现有文件作为备份。</param>
        /// <param name="cancellationToken">A cancellation token. / 取消令牌。</param>
        public static async Task WriteToFileAtomicAsync(IPartitionTable table, string path, bool requireConfirmation = true, string? confirmation = null, bool keepBackup = false, CancellationToken cancellationToken = default)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (path == null) throw new ArgumentNullException(nameof(path));

            ValidateConfirmation(requireConfirmation, confirmation);
            cancellationToken.ThrowIfCancellationRequested();

            string directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp." + Guid.NewGuid().ToString("N"));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, useAsync: true))
                {
                    await Task.Run(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        table.WriteToStream(stream);
                    }, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

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

        /// <summary>
        /// Asynchronously writes a partition table to a seekable stream.
        /// 异步将分区表写入可寻址流。
        /// </summary>
        /// <param name="table">The partition table to write. / 待写入的分区表。</param>
        /// <param name="stream">The destination stream. / 目标流。</param>
        /// <param name="cancellationToken">A cancellation token. / 取消令牌。</param>
        public static async Task WriteToStreamAsync(IPartitionTable table, Stream stream, CancellationToken cancellationToken = default)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                table.WriteToStream(stream);
            }, cancellationToken).ConfigureAwait(false);
        }

        private static void ValidateConfirmation(bool requireConfirmation, string? confirmation)
        {
            if (requireConfirmation)
            {
                string expected = "I_UNDERSTAND_PARTITION_WRITE";
                if (!string.Equals(confirmation, expected, StringComparison.Ordinal))
                {
                    throw new PartitionOperationException("Partition write confirmation token is missing or invalid.", "WRITE_CONFIRMATION_INVALID");
                }
            }
        }
    }
}
