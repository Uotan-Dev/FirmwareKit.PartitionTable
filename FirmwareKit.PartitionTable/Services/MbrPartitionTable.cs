using System;
using System.IO;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Represents a parsed MBR partition table.
    /// 表示已解析的 MBR 分区表。
    /// </summary>
    public sealed class MbrPartitionTable : IPartitionTable, IMutablePartitionTable
    {
        private readonly byte[] _bootstrapCode;
        private readonly MbrPartitionEntry[] _partitions;
        private bool _isMutable;

        internal MbrPartitionTable(PartitionTableParser.MbrDescriptor descriptor, bool mutable)
        {
            Type = PartitionTableType.Mbr;
            _isMutable = mutable;
            Signature = descriptor.Signature;
            _bootstrapCode = descriptor.BootstrapCode ?? Array.Empty<byte>();
            _partitions = new MbrPartitionEntry[4];

            for (int i = 0; i < _partitions.Length; i++)
            {
                _partitions[i] = PartitionTableParser.ClonePartitionEntry(descriptor.Partitions[i]);
            }

            RefreshProtectiveStatus();
        }

        /// <summary>
        /// Gets the partition table kind.
        /// 获取分区表类型。
        /// </summary>
        public PartitionTableType Type { get; }

        /// <summary>
        /// Gets a value indicating whether the table can be edited.
        /// 获取当前表是否可编辑。
        /// </summary>
        public bool IsMutable => _isMutable;

        /// <summary>
        /// Gets the MBR signature value.
        /// 获取 MBR 签名值。
        /// </summary>
        public ushort Signature { get; }

        /// <summary>
        /// Gets a value indicating whether the MBR is the protective GPT variant.
        /// 获取当前 MBR 是否为保护性 GPT 载体。
        /// </summary>
        public bool IsProtectiveGpt { get; private set; }

        /// <summary>
        /// Gets the four MBR partition entries.
        /// 获取 4 个 MBR 分区项。
        /// </summary>
        public MbrPartitionEntry[] Partitions
        {
            get
            {
                var copy = new MbrPartitionEntry[_partitions.Length];
                for (int i = 0; i < _partitions.Length; i++)
                {
                    copy[i] = PartitionTableParser.ClonePartitionEntry(_partitions[i]);
                }

                return copy;
            }
        }

        /// <summary>
        /// Ensures the table is mutable before editing.
        /// 在编辑前确保当前表可写。
        /// </summary>
        public void EnsureMutable()
        {
            if (!IsMutable)
            {
                throw new InvalidOperationException("MBR partition table is read-only and cannot be modified.");
            }
        }

        /// <summary>
        /// Sets whether the table can be edited.
        /// 设置当前表是否可编辑。
        /// </summary>
        /// <param name="mutable">Whether the table should be mutable. / 是否设为可编辑。</param>
        public void SetMutable(bool mutable)
        {
            _isMutable = mutable;
        }

        /// <summary>
        /// Replaces one partition entry.
        /// 替换一个分区项。
        /// </summary>
        /// <param name="index">The zero-based partition index. / 分区索引，从 0 开始。</param>
        /// <param name="entry">The new partition entry. / 新分区项。</param>
        public void SetPartition(int index, MbrPartitionEntry entry)
        {
            EnsureMutable();
            if (index < 0 || index >= _partitions.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            _partitions[index] = PartitionTableParser.ClonePartitionEntry(entry);
            RefreshProtectiveStatus();
        }

        /// <summary>
        /// Clears one partition entry.
        /// 清空一个分区项。
        /// </summary>
        /// <param name="index">The zero-based partition index. / 分区索引，从 0 开始。</param>
        public void RemovePartition(int index)
        {
            EnsureMutable();
            if (index < 0 || index >= _partitions.Length) throw new ArgumentOutOfRangeException(nameof(index));

            _partitions[index] = new MbrPartitionEntry();
            RefreshProtectiveStatus();
        }

        /// <summary>
        /// Writes the table back to a stream.
        /// 将当前表写回到流。
        /// </summary>
        /// <param name="stream">The destination stream. / 目标流。</param>
        public void WriteToStream(Stream stream)
        {
            EnsureMutable();
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek || !stream.CanWrite) throw new NotSupportedException("The stream must be writable and seekable.");

            long originalPosition = stream.Position;
            try
            {
                byte[] buffer = PartitionTableParser.BuildMbrImage(_partitions, _bootstrapCode);
                if (stream.Length != buffer.Length)
                {
                    stream.SetLength(buffer.Length);
                }

                stream.Position = 0;
                stream.Write(buffer, 0, buffer.Length);
                stream.Flush();
            }
            finally
            {
                stream.Position = Math.Min(originalPosition, stream.Length);
            }
        }

        /// <summary>
        /// Writes the table back to a file.
        /// 将当前表写回到文件。
        /// </summary>
        /// <param name="path">The destination path. / 目标路径。</param>
        public void WriteToFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var stream = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            WriteToStream(stream);
        }

        private void RefreshProtectiveStatus()
        {
            IsProtectiveGpt = PartitionTableParser.ContainsProtectivePartition(_partitions);
        }
    }
}
