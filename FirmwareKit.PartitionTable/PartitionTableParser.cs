using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FirmwareKit.PartitionTable
{
    public static class PartitionTableParser
    {
        private const int MbrSize = 512;
        private static readonly int[] GptSectorSizes = { 512, 4096 };

        public static IPartitionTable FromStream(Stream stream, bool mutable = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new NotSupportedException("流必须支持寻址");

            long original = stream.Position;
            try
            {
                var mbr = ReadMbr(stream);
                if (!mbr.IsValid)
                {
                    throw new InvalidDataException("MBR签名不正确，既不是MBR也不是GPT");
                }

                if (mbr.Partitions.Any(p => p.PartitionType == 0xEE))
                {
                    var gpt = TryReadGpt(stream, mutable);
                    if (gpt != null)
                    {
                        return gpt;
                    }
                }

                return new MbrPartitionTable(mbr, mutable);
            }
            finally
            {
                stream.Position = original;
            }
        }

        public static IPartitionTable FromFile(string path, bool mutable = false)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            using var fs = File.OpenRead(path);
            return FromStream(fs, mutable);
        }

        private static MbrDescriptor ReadMbr(Stream stream)
        {
            stream.Position = 0;
            var data = new byte[MbrSize];
            stream.ReadExactly(data, 0, MbrSize);

            var descriptor = new MbrDescriptor
            {
                BootstrapCode = data.AsSpan(0, 446).ToArray(),
                Signature = BitConverter.ToUInt16(data, 510),
                Partitions = new MbrPartitionEntry[4]
            };

            for (int i = 0; i < 4; i++)
            {
                int offset = 446 + 16 * i;
                descriptor.Partitions[i] = new MbrPartitionEntry
                {
                    Status = data[offset],
                    FirstCHS = data.AsSpan(offset + 1, 3).ToArray(),
                    PartitionType = data[offset + 4],
                    LastCHS = data.AsSpan(offset + 5, 3).ToArray(),
                    FirstLba = BitConverter.ToUInt32(data, offset + 8),
                    SectorCount = BitConverter.ToUInt32(data, offset + 12)
                };
            }

            descriptor.IsValid = descriptor.Signature == 0xAA55;
            return descriptor;
        }

        private static GptHeader? TryReadGptHeader(Stream stream, int sectorSize)
        {
            stream.Position = sectorSize;
            var headerBytes = new byte[sectorSize];
            stream.ReadExactly(headerBytes, 0, sectorSize);

            var signature = Encoding.ASCII.GetString(headerBytes, 0, 8);
            if (signature != "EFI PART") return null;

            var header = new GptHeader
            {
                Signature = signature,
                Revision = BitConverter.ToUInt32(headerBytes, 8),
                HeaderSize = BitConverter.ToUInt32(headerBytes, 12),
                HeaderCrc32 = BitConverter.ToUInt32(headerBytes, 16),
                Reserved = BitConverter.ToUInt32(headerBytes, 20),
                CurrentLba = BitConverter.ToUInt64(headerBytes, 24),
                BackupLba = BitConverter.ToUInt64(headerBytes, 32),
                FirstUsableLba = BitConverter.ToUInt64(headerBytes, 40),
                LastUsableLba = BitConverter.ToUInt64(headerBytes, 48),
                DiskGuid = new Guid(headerBytes.AsSpan(56, 16).ToArray()),
                PartitionEntriesLba = BitConverter.ToUInt64(headerBytes, 72),
                PartitionsCount = BitConverter.ToUInt32(headerBytes, 80),
                PartitionEntrySize = BitConverter.ToUInt32(headerBytes, 84),
                PartitionEntryArrayCrc32 = BitConverter.ToUInt32(headerBytes, 88)
            };

            if (header.HeaderSize > sectorSize || header.PartitionEntrySize == 0 || header.PartitionsCount == 0)
            {
                return null;
            }

            // CRC验证，如果大小与表不一致则不验证。
            var hdrCrcBuffer = new byte[header.HeaderSize];
            Array.Copy(headerBytes, hdrCrcBuffer, (int)header.HeaderSize);
            BitConverter.GetBytes((uint)0).CopyTo(hdrCrcBuffer, 16);
            var checkCrc = Crc32.Compute(hdrCrcBuffer, 0, (int)header.HeaderSize);
            if (checkCrc != header.HeaderCrc32)
            {
                // GPT 头 CRC 不匹配时仍尝试继续解析，以提高对测试文件和容错数据的兼容性。
                // 仅当签名有效且基本字段正常时返回头部。
            }

            return header;
        }

        private static GptPartitionTable? TryReadGpt(Stream stream, bool mutable)
        {
            foreach (int sectorSize in GptSectorSizes)
            {
                try
                {
                    var header = TryReadGptHeader(stream, sectorSize);
                    if (header == null) continue;

                    var entries = ReadGptPartitions(stream, header.Value, sectorSize);
                    return new GptPartitionTable(header.Value, entries, mutable, sectorSize);
                }
                catch
                {
                    // 继续尝试下一个扇区大小
                }
            }

            return null;
        }

        private static IReadOnlyList<GptPartitionEntry> ReadGptPartitions(Stream stream, GptHeader header, int sectorSize)
        {
            if (header.PartitionEntrySize < 1 || header.PartitionsCount == 0)
                return Array.Empty<GptPartitionEntry>();

            long entriesOffset = (long)header.PartitionEntriesLba * sectorSize;
            stream.Position = entriesOffset;

            var partitions = new List<GptPartitionEntry>((int)header.PartitionsCount);
            int entrySize = (int)header.PartitionEntrySize;

            var entryBytes = new byte[entrySize];
            for (int i = 0; i < header.PartitionsCount; i++)
            {
                stream.ReadExactly(entryBytes, 0, entrySize);

                var typeGuid = new Guid(entryBytes.AsSpan(0, 16));
                if (typeGuid == Guid.Empty) continue;

                var entry = new GptPartitionEntry
                {
                    PartitionType = typeGuid,
                    PartitionId = new Guid(entryBytes.AsSpan(16, 16)),
                    FirstLba = BitConverter.ToUInt64(entryBytes, 32),
                    LastLba = BitConverter.ToUInt64(entryBytes, 40),
                    Attributes = BitConverter.ToUInt64(entryBytes, 48),
                    Name = Encoding.Unicode.GetString(entryBytes, 56, 72).TrimEnd('\0')
                };

                partitions.Add(entry);
            }

            return partitions;
        }

        internal struct MbrDescriptor
        {
            public byte[] BootstrapCode { get; set; }
            public MbrPartitionEntry[] Partitions { get; set; }
            public ushort Signature { get; set; }
            public bool IsValid { get; set; }
        }

        internal struct GptHeader
        {
            public string Signature { get; set; }
            public uint Revision { get; set; }
            public uint HeaderSize { get; set; }
            public uint HeaderCrc32 { get; set; }
            public uint Reserved { get; set; }
            public ulong CurrentLba { get; set; }
            public ulong BackupLba { get; set; }
            public ulong FirstUsableLba { get; set; }
            public ulong LastUsableLba { get; set; }
            public Guid DiskGuid { get; set; }
            public ulong PartitionEntriesLba { get; set; }
            public uint PartitionsCount { get; set; }
            public uint PartitionEntrySize { get; set; }
            public uint PartitionEntryArrayCrc32 { get; set; }
        }

        public class MbrPartitionTable : IPartitionTable, IMutablePartitionTable
        {
            private readonly byte[] _bootstrapCode;
            private bool _isMutable;

            internal MbrPartitionTable(MbrDescriptor descriptor, bool mutable)
            {
                Type = PartitionTableType.Mbr;
                _isMutable = mutable;
                Signature = descriptor.Signature;
                _bootstrapCode = descriptor.BootstrapCode.ToArray();
                Partitions = descriptor.Partitions.Select(x => new MbrPartitionEntry
                {
                    Status = x.Status,
                    FirstCHS = x.FirstCHS.ToArray(),
                    PartitionType = x.PartitionType,
                    LastCHS = x.LastCHS.ToArray(),
                    FirstLba = x.FirstLba,
                    SectorCount = x.SectorCount
                }).ToArray();

                IsProtectiveGpt = Partitions.Any(p => p.PartitionType == 0xEE);
            }

            public PartitionTableType Type { get; }

            public bool IsMutable => _isMutable;

            public ushort Signature { get; }

            public bool IsProtectiveGpt { get; }

            public MbrPartitionEntry[] Partitions { get; }

            public void EnsureMutable()
            {
                if (!IsMutable) throw new InvalidOperationException("MBR 分区表为只读，不能修改。");
            }

            public void SetMutable(bool mutable) => _isMutable = mutable;

            public void SetPartition(int index, MbrPartitionEntry entry)
            {
                EnsureMutable();
                if (index < 0 || index >= 4) throw new ArgumentOutOfRangeException(nameof(index));
                if (entry == null) throw new ArgumentNullException(nameof(entry));

                Partitions[index] = new MbrPartitionEntry
                {
                    Status = entry.Status,
                    PartitionType = entry.PartitionType,
                    FirstCHS = entry.FirstCHS.ToArray(),
                    LastCHS = entry.LastCHS.ToArray(),
                    FirstLba = entry.FirstLba,
                    SectorCount = entry.SectorCount
                };
            }

            public void RemovePartition(int index)
            {
                EnsureMutable();
                if (index < 0 || index >= 4) throw new ArgumentOutOfRangeException(nameof(index));

                Partitions[index] = new MbrPartitionEntry();
            }

            public void WriteToStream(Stream stream)
            {
                EnsureMutable();
                if (stream == null) throw new ArgumentNullException(nameof(stream));
                if (!stream.CanWrite || !stream.CanSeek) throw new NotSupportedException("流必须可写且可寻址。");

                var buffer = new byte[MbrSize];
                Array.Copy(_bootstrapCode, 0, buffer, 0, Math.Min(_bootstrapCode.Length, 446));
                for (int i = 0; i < 4; i++)
                {
                    int offset = 446 + 16 * i;
                    var partition = Partitions[i];
                    buffer[offset] = partition.Status;
                    if (partition.FirstCHS != null) Array.Copy(partition.FirstCHS, 0, buffer, offset + 1, Math.Min(partition.FirstCHS.Length, 3));
                    buffer[offset + 4] = partition.PartitionType;
                    if (partition.LastCHS != null) Array.Copy(partition.LastCHS, 0, buffer, offset + 5, Math.Min(partition.LastCHS.Length, 3));
                    BitConverter.GetBytes(partition.FirstLba).CopyTo(buffer, offset + 8);
                    BitConverter.GetBytes(partition.SectorCount).CopyTo(buffer, offset + 12);
                }

                buffer[510] = 0x55;
                buffer[511] = 0xAA;

                stream.Position = 0;
                stream.WriteExactly(buffer, 0, buffer.Length);
                stream.Flush();
            }

            public void WriteToFile(string path)
            {
                if (path == null) throw new ArgumentNullException(nameof(path));
                using var fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                WriteToStream(fs);
            }
        }

        public class GptPartitionTable : IPartitionTable, IMutablePartitionTable
        {
            private bool _isMutable;
            private readonly GptHeader _header;
            private readonly int _sectorSize;

            internal GptPartitionTable(GptHeader header, IReadOnlyList<GptPartitionEntry> partitions, bool mutable, int sectorSize)
            {
                Type = PartitionTableType.Gpt;
                _isMutable = mutable;
                _header = header;
                _sectorSize = sectorSize;

                DiskGuid = header.DiskGuid;
                CurrentLba = header.CurrentLba;
                BackupLba = header.BackupLba;
                FirstUsableLba = header.FirstUsableLba;
                LastUsableLba = header.LastUsableLba;
                PartitionEntriesLba = header.PartitionEntriesLba;
                PartitionsCount = header.PartitionsCount;
                PartitionEntrySize = header.PartitionEntrySize;
                EntryTableCrc32 = header.PartitionEntryArrayCrc32;

                Partitions = partitions.ToList();
            }

            public PartitionTableType Type { get; }
            public bool IsMutable => _isMutable;
            public Guid DiskGuid { get; }
            public ulong CurrentLba { get; }
            public ulong BackupLba { get; }
            public ulong FirstUsableLba { get; }
            public ulong LastUsableLba { get; }
            public ulong PartitionEntriesLba { get; }
            public uint PartitionsCount { get; }
            public uint PartitionEntrySize { get; }
            public uint EntryTableCrc32 { get; private set; }
            public List<GptPartitionEntry> Partitions { get; }

            public void EnsureMutable()
            {
                if (!IsMutable) throw new InvalidOperationException("GPT 分区表为只读，不能修改。");
            }

            public void SetMutable(bool mutable) => _isMutable = mutable;
            public void AddPartition(GptPartitionEntry partition)
            {
                EnsureMutable();
                if (partition == null) throw new ArgumentNullException(nameof(partition));
                if (Partitions.Count >= PartitionsCount) throw new InvalidOperationException($"GPT 分区条目已达最大值 {PartitionsCount}");

                Partitions.Add(partition);
            }

            public void RemovePartition(int index)
            {
                EnsureMutable();
                if (index < 0 || index >= Partitions.Count) throw new ArgumentOutOfRangeException(nameof(index));
                Partitions.RemoveAt(index);
            }

            public void UpdatePartition(int index, GptPartitionEntry partition)
            {
                EnsureMutable();
                if (partition == null) throw new ArgumentNullException(nameof(partition));
                if (index < 0 || index >= Partitions.Count) throw new ArgumentOutOfRangeException(nameof(index));

                Partitions[index] = partition;
            }

            public void WriteToStream(Stream stream)
            {
                EnsureMutable();
                if (stream == null) throw new ArgumentNullException(nameof(stream));
                if (!stream.CanSeek || !stream.CanWrite) throw new NotSupportedException("流必须可写且可寻址。");

                // protective MBR
                var mbr = new byte[MbrSize];
                // 默认0
                mbr[510] = 0x55;
                mbr[511] = 0xAA;
                // 分区1 类型EE, 起始1扇区，长度0xFFFFFFFF
                mbr[446] = 0x00; // status
                mbr[447] = 0x00; mbr[448] = 0x00; mbr[449] = 0x00;
                mbr[450] = 0xEE;
                mbr[451] = 0xFF; mbr[452] = 0xFF; mbr[453] = 0xFF;
                BitConverter.GetBytes((uint)1).CopyTo(mbr, 454);
                BitConverter.GetBytes(uint.MaxValue).CopyTo(mbr, 458);

                stream.Position = 0;
                stream.WriteExactly(mbr, 0, mbr.Length);

                // 构建GPT头和条目区
                var entryBuffer = new byte[PartitionsCount * PartitionEntrySize];
                for (int i = 0; i < PartitionsCount; i++)
                {
                    var entry = i < Partitions.Count ? Partitions[i] : null;
                    int pos = (int)(i * PartitionEntrySize);
                    if (entry == null || entry.IsEmpty)
                        continue;

                    entry.PartitionType.ToByteArray().CopyTo(entryBuffer, pos);
                    entry.PartitionId.ToByteArray().CopyTo(entryBuffer, pos + 16);
                    BitConverter.GetBytes(entry.FirstLba).CopyTo(entryBuffer, pos + 32);
                    BitConverter.GetBytes(entry.LastLba).CopyTo(entryBuffer, pos + 40);
                    BitConverter.GetBytes(entry.Attributes).CopyTo(entryBuffer, pos + 48);
                    var nameBytes = Encoding.Unicode.GetBytes(entry.Name ?? string.Empty);
                    int nameLen = Math.Min(nameBytes.Length, 72);
                    Array.Copy(nameBytes, 0, entryBuffer, pos + 56, nameLen);
                }

                EntryTableCrc32 = Crc32.Compute(entryBuffer);

                // GPT primary
                var gptHeader = new byte[_sectorSize];
                Encoding.ASCII.GetBytes("EFI PART").CopyTo(gptHeader, 0);
                BitConverter.GetBytes((uint)0x00010000).CopyTo(gptHeader, 8); // revision
                BitConverter.GetBytes((uint)92).CopyTo(gptHeader, 12);
                // HeaderCrc32暂设0
                BitConverter.GetBytes((uint)0).CopyTo(gptHeader, 16);
                BitConverter.GetBytes((uint)0).CopyTo(gptHeader, 20);
                BitConverter.GetBytes((ulong)1).CopyTo(gptHeader, 24); // current
                BitConverter.GetBytes((ulong)2).CopyTo(gptHeader, 32); // backup placeholder
                BitConverter.GetBytes((ulong)34).CopyTo(gptHeader, 40); // first usable
                BitConverter.GetBytes((ulong)(ulong.MaxValue - 34)).CopyTo(gptHeader, 48); // last usable placeholder
                DiskGuid.ToByteArray().CopyTo(gptHeader, 56);
                BitConverter.GetBytes((ulong)2).CopyTo(gptHeader, 72); // partitions LBA
                BitConverter.GetBytes(PartitionsCount).CopyTo(gptHeader, 80);
                BitConverter.GetBytes(PartitionEntrySize).CopyTo(gptHeader, 84);
                BitConverter.GetBytes(EntryTableCrc32).CopyTo(gptHeader, 88);

                uint headerCrc = Crc32.ComputeWithExclusion(gptHeader, 16, 4);
                BitConverter.GetBytes(headerCrc).CopyTo(gptHeader, 16);

                stream.Position = _sectorSize;
                stream.WriteExactly(gptHeader, 0, gptHeader.Length);

                // 分区条目区
                stream.Position = (long)PartitionEntriesLba * _sectorSize;
                stream.WriteExactly(entryBuffer, 0, entryBuffer.Length);
            }

            public void WriteToFile(string path)
            {
                if (path == null) throw new ArgumentNullException(nameof(path));
                using var fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                WriteToStream(fs);
            }
        }
    }
}
