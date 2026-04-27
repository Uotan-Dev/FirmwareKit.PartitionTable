using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using System;
using System.Collections.Generic;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// A fluent builder for creating partition tables from scratch.
    /// 用于从零创建分区表的流式构建器。
    /// </summary>
    public sealed class PartitionTableBuilder
    {
        private PartitionTableType _kind;
        private int _sectorSize;
        private Guid _diskGuid;
        private ulong _firstUsableLba;
        private ulong _lastUsableLba;
        private uint _gptPartitionCount;
        private uint _gptPartitionEntrySize;

        private PartitionTableBuilder()
        {
            _sectorSize = 512;
            _diskGuid = Guid.NewGuid();
            _gptPartitionCount = 128;
            _gptPartitionEntrySize = 128;
        }

        /// <summary>
        /// Creates a builder for a GPT partition table.
        /// 创建 GPT 分区表的构建器。
        /// </summary>
        public static PartitionTableBuilder CreateGpt()
        {
            return new PartitionTableBuilder { _kind = PartitionTableType.Gpt };
        }

        /// <summary>
        /// Creates a builder for an MBR partition table.
        /// 创建 MBR 分区表的构建器。
        /// </summary>
        public static PartitionTableBuilder CreateMbr()
        {
            return new PartitionTableBuilder { _kind = PartitionTableType.Mbr };
        }

        /// <summary>
        /// Creates a builder for an Amlogic EPT partition table.
        /// 创建 Amlogic EPT 分区表的构建器。
        /// </summary>
        public static PartitionTableBuilder CreateAmlogicEpt()
        {
            return new PartitionTableBuilder { _kind = PartitionTableType.AmlogicEpt };
        }

        /// <summary>
        /// Sets the sector size in bytes.
        /// 设置扇区大小（字节）。
        /// </summary>
        public PartitionTableBuilder WithSectorSize(int sectorSize)
        {
            if (sectorSize <= 0) throw new ArgumentOutOfRangeException(nameof(sectorSize));
            _sectorSize = sectorSize;
            return this;
        }

        /// <summary>
        /// Sets the disk GUID for GPT tables.
        /// 设置 GPT 表的磁盘 GUID。
        /// </summary>
        public PartitionTableBuilder WithDiskGuid(Guid diskGuid)
        {
            _diskGuid = diskGuid;
            return this;
        }

        /// <summary>
        /// Sets the usable LBA range for GPT tables.
        /// 设置 GPT 表的可用 LBA 范围。
        /// </summary>
        public PartitionTableBuilder WithUsableLbaRange(ulong firstUsableLba, ulong lastUsableLba)
        {
            if (lastUsableLba < firstUsableLba) throw new ArgumentOutOfRangeException(nameof(lastUsableLba));
            _firstUsableLba = firstUsableLba;
            _lastUsableLba = lastUsableLba;
            return this;
        }

        /// <summary>
        /// Sets the GPT partition slot count and entry size.
        /// 设置 GPT 分区槽位数和项大小。
        /// </summary>
        public PartitionTableBuilder WithGptPartitionSlots(uint count, uint entrySize = 128)
        {
            if (count == 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (entrySize < 56) throw new ArgumentOutOfRangeException(nameof(entrySize));
            _gptPartitionCount = count;
            _gptPartitionEntrySize = entrySize;
            return this;
        }

        /// <summary>
        /// Builds the partition table.
        /// 构建分区表。
        /// </summary>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <returns>The newly created partition table.</returns>
        public IPartitionTable Build(bool mutable = true)
        {
            switch (_kind)
            {
                case PartitionTableType.Gpt:
                    return BuildGpt(mutable);
                case PartitionTableType.Mbr:
                    return BuildMbr(mutable);
                case PartitionTableType.AmlogicEpt:
                    return BuildAmlogicEpt(mutable);
                default:
                    throw new InvalidOperationException($"Unsupported partition table kind: {_kind}");
            }
        }

        private IPartitionTable BuildGpt(bool mutable)
        {
            ulong entryTableSectors = ((ulong)_gptPartitionCount * _gptPartitionEntrySize + (ulong)_sectorSize - 1) / (ulong)_sectorSize;
            ulong minimumTotalSectors = 3 + (entryTableSectors * 2);
            ulong totalSectors = Math.Max(128UL, minimumTotalSectors);
            ulong backupLba = totalSectors - 1;
            ulong backupEntriesLba = backupLba - entryTableSectors;

            ulong firstUsable = _firstUsableLba > 0 ? _firstUsableLba : 34;
            ulong lastUsable = _lastUsableLba > 0 ? _lastUsableLba : (backupEntriesLba > 0 ? backupEntriesLba - 1 : 0);
            if (lastUsable >= backupEntriesLba)
            {
                lastUsable = backupEntriesLba > 0 ? backupEntriesLba - 1 : 0;
            }

            var header = new PartitionTableParser.GptHeader
            {
                FirstUsableLba = firstUsable,
                LastUsableLba = lastUsable,
                DiskGuid = _diskGuid,
                PartitionsCount = _gptPartitionCount,
                PartitionEntrySize = _gptPartitionEntrySize
            };

            var entryBuffer = PartitionTableParser.BuildGptEntryBuffer(new GptPartitionEntry[0], _gptPartitionCount, _gptPartitionEntrySize);
            uint entryBufferCrc = FirmwareKit.PartitionTable.Util.Crc32.Compute(entryBuffer);

            var buffer = new byte[checked((int)(totalSectors * (ulong)_sectorSize))];
            byte[] protectiveMbr = PartitionTableParser.BuildProtectiveMbr();
            Buffer.BlockCopy(protectiveMbr, 0, buffer, 0, Math.Min(protectiveMbr.Length, buffer.Length));

            byte[] primaryHeader = PartitionTableParser.BuildGptHeader(header, currentLba: 1, backupLba: backupLba, partitionEntriesLba: 2, partitionEntryArrayCrc32: entryBufferCrc, sectorSize: _sectorSize);
            Buffer.BlockCopy(primaryHeader, 0, buffer, _sectorSize, primaryHeader.Length);
            Buffer.BlockCopy(entryBuffer, 0, buffer, checked((int)(2 * (ulong)_sectorSize)), entryBuffer.Length);
            Buffer.BlockCopy(entryBuffer, 0, buffer, checked((int)(backupEntriesLba * (ulong)_sectorSize)), entryBuffer.Length);

            byte[] backupHeader = PartitionTableParser.BuildGptHeader(header, currentLba: backupLba, backupLba: 1, partitionEntriesLba: backupEntriesLba, partitionEntryArrayCrc32: entryBufferCrc, sectorSize: _sectorSize);
            Buffer.BlockCopy(backupHeader, 0, buffer, checked((int)(backupLba * (ulong)_sectorSize)), backupHeader.Length);

            using var stream = new System.IO.MemoryStream(buffer, writable: false);
            return PartitionTableReader.FromStream(stream, mutable, _sectorSize);
        }

        private IPartitionTable BuildMbr(bool mutable)
        {
            var buffer = PartitionTableParser.BuildMbrImage(new MbrPartitionEntry[4], null!);
            using var stream = new System.IO.MemoryStream(buffer, writable: false);
            return PartitionTableReader.FromStream(stream, mutable);
        }

        private IPartitionTable BuildAmlogicEpt(bool mutable)
        {
            return new AmlogicPartitionTable(new List<AmlogicPartitionEntry>(), mutable);
        }
    }
}
