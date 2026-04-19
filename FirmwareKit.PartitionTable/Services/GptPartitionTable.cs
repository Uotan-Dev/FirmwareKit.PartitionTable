using System;
using System.Collections.Generic;
using System.IO;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Represents a parsed GPT partition table.
    /// 表示已解析的 GPT 分区表。
    /// </summary>
    public sealed class GptPartitionTable : IPartitionTable, IMutablePartitionTable
    {
        private readonly PartitionTableParser.GptHeader _header;
        private readonly int _sectorSize;
        private bool _isMutable;

        internal GptPartitionTable(PartitionTableParser.GptHeader header, List<GptPartitionEntry> partitions, bool mutable, int sectorSize, bool headerCrcValid, bool entryTableCrcValid)
        {
            Type = PartitionTableType.Gpt;
            _header = header;
            _sectorSize = sectorSize;
            _isMutable = mutable;
            Partitions = partitions ?? new List<GptPartitionEntry>();
            DiskGuid = header.DiskGuid;
            CurrentLba = header.CurrentLba;
            BackupLba = header.BackupLba;
            FirstUsableLba = header.FirstUsableLba;
            LastUsableLba = header.LastUsableLba;
            PartitionEntriesLba = header.PartitionEntriesLba;
            PartitionsCount = header.PartitionsCount;
            PartitionEntrySize = header.PartitionEntrySize;
            EntryTableCrc32 = header.PartitionEntryArrayCrc32;
            HeaderCrc32 = header.HeaderCrc32;
            IsHeaderCrcValid = headerCrcValid;
            IsEntryTableCrcValid = entryTableCrcValid;
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
        /// Gets the disk GUID.
        /// 获取磁盘 GUID。
        /// </summary>
        public Guid DiskGuid { get; }

        /// <summary>
        /// Gets the current header LBA.
        /// 获取当前头部所在 LBA。
        /// </summary>
        public ulong CurrentLba { get; }

        /// <summary>
        /// Gets the backup header LBA.
        /// 获取备份头所在 LBA。
        /// </summary>
        public ulong BackupLba { get; }

        /// <summary>
        /// Gets the first usable LBA.
        /// 获取第一个可用 LBA。
        /// </summary>
        public ulong FirstUsableLba { get; }

        /// <summary>
        /// Gets the last usable LBA.
        /// 获取最后一个可用 LBA。
        /// </summary>
        public ulong LastUsableLba { get; }

        /// <summary>
        /// Gets the LBA of the partition entry array.
        /// 获取分区项数组的起始 LBA。
        /// </summary>
        public ulong PartitionEntriesLba { get; }

        /// <summary>
        /// Gets the maximum number of partition entries.
        /// 获取分区项最大数量。
        /// </summary>
        public uint PartitionsCount { get; }

        /// <summary>
        /// Gets the size of one GPT entry.
        /// 获取单个 GPT 分区项大小。
        /// </summary>
        public uint PartitionEntrySize { get; }

        /// <summary>
        /// Gets the raw header CRC-32 value.
        /// 获取头部原始 CRC-32 值。
        /// </summary>
        public uint HeaderCrc32 { get; }

        /// <summary>
        /// Gets the raw partition entry array CRC-32 value.
        /// 获取分区项数组原始 CRC-32 值。
        /// </summary>
        public uint EntryTableCrc32 { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the header CRC matches the source data.
        /// 获取头部 CRC 是否与源数据匹配。
        /// </summary>
        public bool IsHeaderCrcValid { get; }

        /// <summary>
        /// Gets a value indicating whether the entry table CRC matches the source data.
        /// 获取分区项表 CRC 是否与源数据匹配。
        /// </summary>
        public bool IsEntryTableCrcValid { get; private set; }

        /// <summary>
        /// Gets the sector size that was used to parse the table.
        /// 获取解析时使用的扇区大小。
        /// </summary>
        public int SectorSize => _sectorSize;

        /// <summary>
        /// Gets the parsed GPT partition entries.
        /// 获取已解析的 GPT 分区项集合。
        /// </summary>
        public List<GptPartitionEntry> Partitions { get; }

        /// <summary>
        /// Ensures the table is mutable before editing.
        /// 在编辑前确保当前表可写。
        /// </summary>
        public void EnsureMutable()
        {
            if (!IsMutable)
            {
                throw new InvalidOperationException("GPT partition table is read-only and cannot be modified.");
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
        public void AddPartition(GptPartitionEntry partition)
        {
            EnsureMutable();
            if (partition == null) throw new ArgumentNullException(nameof(partition));
            if (Partitions.Count >= PartitionsCount) throw new InvalidOperationException($"GPT partition entry count has reached the maximum value of {PartitionsCount}.");

            Partitions.Add(PartitionTableParser.ClonePartitionEntry(partition));
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
        }

        /// <summary>
        /// Replaces one partition entry.
        /// 替换一个分区项。
        /// </summary>
        /// <param name="index">The zero-based partition index. / 分区索引，从 0 开始。</param>
        /// <param name="partition">The new partition entry. / 新分区项。</param>
        public void UpdatePartition(int index, GptPartitionEntry partition)
        {
            EnsureMutable();
            if (partition == null) throw new ArgumentNullException(nameof(partition));
            if (index < 0 || index >= Partitions.Count) throw new ArgumentOutOfRangeException(nameof(index));

            Partitions[index] = PartitionTableParser.ClonePartitionEntry(partition);
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

            ValidateWriteLayout();

            long originalPosition = stream.Position;
            try
            {
                byte[] entryBuffer = PartitionTableParser.BuildGptEntryBuffer(Partitions, PartitionsCount, PartitionEntrySize);
                uint entryBufferCrc32 = Crc32.Compute(entryBuffer);
                byte[] primaryHeader = PartitionTableParser.BuildGptHeader(_header, CurrentLba, BackupLba, PartitionEntriesLba, entryBufferCrc32, _sectorSize);

                ulong entryTableSectors = GetEntryTableSectorCount();
                ulong backupEntriesLba = BackupLba - entryTableSectors;
                byte[] backupHeader = PartitionTableParser.BuildGptHeader(_header, BackupLba, CurrentLba, backupEntriesLba, entryBufferCrc32, _sectorSize);

                long requiredLength = (long)(BackupLba + 1) * _sectorSize;
                if (stream.Length != requiredLength)
                {
                    stream.SetLength(requiredLength);
                }

                byte[] protectiveMbr = PartitionTableParser.BuildProtectiveMbr();

                stream.Position = 0;
                stream.Write(protectiveMbr, 0, protectiveMbr.Length);

                stream.Position = (long)CurrentLba * _sectorSize;
                stream.Write(primaryHeader, 0, primaryHeader.Length);

                stream.Position = (long)PartitionEntriesLba * _sectorSize;
                stream.Write(entryBuffer, 0, entryBuffer.Length);

                stream.Position = (long)backupEntriesLba * _sectorSize;
                stream.Write(entryBuffer, 0, entryBuffer.Length);

                stream.Position = (long)BackupLba * _sectorSize;
                stream.Write(backupHeader, 0, backupHeader.Length);

                stream.Flush();

                EntryTableCrc32 = entryBufferCrc32;
                IsEntryTableCrcValid = true;
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

        private ulong GetEntryTableSectorCount()
        {
            ulong tableBytes = (ulong)PartitionsCount * PartitionEntrySize;
            return (tableBytes + (ulong)_sectorSize - 1) / (ulong)_sectorSize;
        }

        private void ValidateWriteLayout()
        {
            if (PartitionEntrySize == 0 || PartitionsCount == 0)
            {
                throw new InvalidOperationException("GPT header is invalid and cannot be written.");
            }

            if (BackupLba <= GetEntryTableSectorCount())
            {
                throw new InvalidOperationException("GPT backup header position is invalid and cannot be written.");
            }
        }
    }
}
