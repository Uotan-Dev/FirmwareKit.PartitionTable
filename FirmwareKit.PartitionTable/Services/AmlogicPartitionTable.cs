using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using FirmwareKit.PartitionTable.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// Represents a parsed Amlogic proprietary EPT partition table.
    /// 表示已解析的 Amlogic 私有 EPT 分区表。
    /// </summary>
    public sealed class AmlogicPartitionTable : IPartitionTable, IMutablePartitionTable
    {
        private readonly uint[] _versionWords;
        private readonly uint _recordedChecksum;
        private bool _isMutable;

        internal AmlogicPartitionTable(uint[] versionWords, uint recordedChecksum, List<AmlogicPartitionEntry> partitions, bool mutable, bool checksumValid)
        {
            Type = PartitionTableType.AmlogicEpt;
            _versionWords = versionWords ?? new uint[3];
            _recordedChecksum = recordedChecksum;
            _isMutable = mutable;
            IsChecksumValid = checksumValid;
            Partitions = partitions ?? new List<AmlogicPartitionEntry>();
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
        /// Gets the parsed partition entries.
        /// 获取已解析的分区项集合。
        /// </summary>
        public List<AmlogicPartitionEntry> Partitions { get; }

        /// <summary>
        /// Gets a value indicating whether the recorded checksum matches source data.
        /// 获取记录校验和是否与源数据匹配。
        /// </summary>
        public bool IsChecksumValid { get; private set; }

        /// <summary>
        /// Gets the table version words in little-endian representation.
        /// 获取小端表示的版本字。
        /// </summary>
        public IReadOnlyList<uint> VersionWords => _versionWords;

        /// <summary>
        /// Gets the recorded checksum value from source table.
        /// 获取源表中的记录校验和值。
        /// </summary>
        public uint RecordedChecksum => _recordedChecksum;

        /// <summary>
        /// Ensures the table is mutable before editing.
        /// 在编辑前确保当前表可写。
        /// </summary>
        public void EnsureMutable()
        {
            if (!IsMutable)
            {
                throw new InvalidOperationException("Amlogic EPT partition table is read-only and cannot be modified.");
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
        /// Adds a partition entry.
        /// 添加一个分区项。
        /// </summary>
        /// <param name="partition">The new partition entry. / 新分区项。</param>
        public void AddPartition(AmlogicPartitionEntry partition)
        {
            EnsureMutable();
            if (partition == null) throw new ArgumentNullException(nameof(partition));
            if (Partitions.Count >= AmlogicPartitionTableSupport.PartitionSlotCount)
            {
                throw new InvalidOperationException($"Amlogic EPT partition count has reached the maximum value of {AmlogicPartitionTableSupport.PartitionSlotCount}.");
            }

            Partitions.Add(PartitionTableParser.ClonePartitionEntry(partition));
            IsChecksumValid = false;
        }

        /// <summary>
        /// Removes a partition entry.
        /// 移除一个分区项。
        /// </summary>
        /// <param name="index">The zero-based partition index. / 分区索引，从 0 开始。</param>
        public void RemovePartition(int index)
        {
            EnsureMutable();
            if (index < 0 || index >= Partitions.Count) throw new ArgumentOutOfRangeException(nameof(index));

            Partitions.RemoveAt(index);
            IsChecksumValid = false;
        }

        /// <summary>
        /// Replaces one partition entry.
        /// 替换一个分区项。
        /// </summary>
        /// <param name="index">The zero-based partition index. / 分区索引，从 0 开始。</param>
        /// <param name="partition">The new partition entry. / 新分区项。</param>
        public void UpdatePartition(int index, AmlogicPartitionEntry partition)
        {
            EnsureMutable();
            if (partition == null) throw new ArgumentNullException(nameof(partition));
            if (index < 0 || index >= Partitions.Count) throw new ArgumentOutOfRangeException(nameof(index));

            Partitions[index] = PartitionTableParser.ClonePartitionEntry(partition);
            IsChecksumValid = false;
        }

        /// <summary>
        /// Writes the table back to a stream after validating Amlogic-specific constraints.
        /// 将当前表写回到流。
        /// </summary>
        /// <param name="stream">The destination stream. / 目标流。</param>
        public void WriteToStream(Stream stream)
        {
            EnsureMutable();
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek || !stream.CanWrite) throw new NotSupportedException("The stream must be writable and seekable.");
            if (Partitions.Count == 0)
            {
                throw new InvalidOperationException("Amlogic EPT partition table must contain at least one partition.");
            }
            if (Partitions.Count > AmlogicPartitionTableSupport.PartitionSlotCount)
            {
                throw new InvalidOperationException($"Amlogic EPT partition count has reached the maximum value of {AmlogicPartitionTableSupport.PartitionSlotCount}.");
            }

            for (int i = 0; i < Partitions.Count; i++)
            {
                if (!AmlogicPartitionTableSupport.IsValidPartitionName(Partitions[i].Name))
                {
                    throw new InvalidOperationException("Amlogic EPT partition names must be 1-15 ASCII characters from [A-Za-z0-9_-].");
                }
            }

            long originalPosition = stream.Position;
            try
            {
                byte[] buffer = PartitionTableParser.BuildAmlogicEptImage(_versionWords, Partitions);
                if (stream.Length < buffer.Length)
                {
                    stream.SetLength(buffer.Length);
                }

                stream.Position = 0;
                stream.Write(buffer, 0, buffer.Length);
                stream.Flush();
                IsChecksumValid = true;
            }
            finally
            {
                stream.Position = Math.Min(originalPosition, stream.Length);
            }
        }

        /// <summary>
        /// Writes the table back to a file after validating Amlogic-specific constraints.
        /// 将当前表写回到文件。
        /// </summary>
        /// <param name="path">The destination path. / 目标路径。</param>
        public void WriteToFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            WriteToStream(stream);
        }
    }
}
