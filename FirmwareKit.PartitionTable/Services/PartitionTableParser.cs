using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Reads and writes MBR and GPT partition tables.
    /// 读取和写入 MBR 与 GPT 分区表。
    /// </summary>
    public static class PartitionTableParser
    {
        private const int MbrSize = 512;
        private const int MbrPartitionCount = 4;
        private const ushort MbrSignature = 0xAA55;
        private const string GptSignature = "EFI PART";
        private const uint GptRevision = 0x00010000;
        private const uint GptHeaderSize = 92;
        private static readonly int[] DefaultSectorSizes = { 512, 1024, 2048, 4096 };

        /// <summary>
        /// Reads a partition table from a seekable stream.
        /// 从可寻址流中读取分区表。
        /// </summary>
        /// <param name="stream">The source stream. / 源流。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <returns>The parsed partition table. / 解析后的分区表。</returns>
        public static IPartitionTable FromStream(Stream stream, bool mutable = false)
        {
            return FromStream(stream, mutable, null);
        }

        /// <summary>
        /// Reads a partition table from a seekable stream and prefers the supplied sector size when probing GPT.
        /// 从可寻址流中读取分区表，并在探测 GPT 时优先使用指定扇区大小。
        /// </summary>
        /// <param name="stream">The source stream. / 源流。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <param name="sectorSize">The preferred sector size, or <see langword="null" /> to auto-detect. / 首选扇区大小，或传 <see langword="null" /> 自动检测。</param>
        /// <returns>The parsed partition table. / 解析后的分区表。</returns>
        public static IPartitionTable FromStream(Stream stream, bool mutable, int? sectorSize)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new NotSupportedException("The stream must support seeking.");

            long originalPosition = stream.Position;
            try
            {
                var gpt = TryReadGpt(stream, mutable, sectorSize);
                if (gpt != null)
                {
                    return gpt;
                }

                if (TryReadMbr(stream, out var mbr))
                {
                    return new MbrPartitionTable(mbr, mutable);
                }

                throw new InvalidDataException("No valid MBR or GPT partition table could be identified from the stream.");
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        /// <summary>
        /// Reads a partition table from a file.
        /// 从文件中读取分区表。
        /// </summary>
        /// <param name="path">The file path. / 文件路径。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <returns>The parsed partition table. / 解析后的分区表。</returns>
        public static IPartitionTable FromFile(string path, bool mutable = false)
        {
            return FromFile(path, mutable, null);
        }

        /// <summary>
        /// Reads a partition table from a file and prefers the supplied sector size when probing GPT.
        /// 从文件中读取分区表，并在探测 GPT 时优先使用指定扇区大小。
        /// </summary>
        /// <param name="path">The file path. / 文件路径。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <param name="sectorSize">The preferred sector size, or <see langword="null" /> to auto-detect. / 首选扇区大小，或传 <see langword="null" /> 自动检测。</param>
        /// <returns>The parsed partition table. / 解析后的分区表。</returns>
        public static IPartitionTable FromFile(string path, bool mutable, int? sectorSize)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var stream = File.OpenRead(path);
            return FromStream(stream, mutable, sectorSize);
        }

        private static bool TryReadMbr(Stream stream, out MbrDescriptor descriptor)
        {
            descriptor = default;

            if (!TryReadBytes(stream, 0, MbrSize, out var data))
            {
                return false;
            }

            var partitions = new MbrPartitionEntry[MbrPartitionCount];
            for (int i = 0; i < MbrPartitionCount; i++)
            {
                int offset = 446 + (16 * i);
                partitions[i] = new MbrPartitionEntry
                {
                    Status = data[offset],
                    FirstCHS = CopyBytes(data, offset + 1, 3),
                    PartitionType = data[offset + 4],
                    LastCHS = CopyBytes(data, offset + 5, 3),
                    FirstLba = ReadUInt32LittleEndian(data, offset + 8),
                    SectorCount = ReadUInt32LittleEndian(data, offset + 12)
                };
            }

            descriptor = new MbrDescriptor
            {
                BootstrapCode = CopyBytes(data, 0, 446),
                Partitions = partitions,
                Signature = ReadUInt16LittleEndian(data, 510),
                IsValid = ReadUInt16LittleEndian(data, 510) == MbrSignature
            };

            return descriptor.IsValid;
        }

        private static GptPartitionTable? TryReadGpt(Stream stream, bool mutable, int? preferredSectorSize)
        {
            if (preferredSectorSize.HasValue)
            {
                if (preferredSectorSize.Value <= 0) throw new ArgumentOutOfRangeException(nameof(preferredSectorSize));
                return TryReadGptWithSectorSize(stream, mutable, preferredSectorSize.Value);
            }

            for (int i = 0; i < DefaultSectorSizes.Length; i++)
            {
                var table = TryReadGptWithSectorSize(stream, mutable, DefaultSectorSizes[i]);
                if (table != null)
                {
                    return table;
                }
            }

            return null;
        }

        private static GptPartitionTable? TryReadGptWithSectorSize(Stream stream, bool mutable, int sectorSize)
        {
            if (!TryReadBytes(stream, sectorSize, sectorSize, out var headerBytes))
            {
                return null;
            }

            if (!string.Equals(Encoding.ASCII.GetString(headerBytes, 0, 8), GptSignature, StringComparison.Ordinal))
            {
                return null;
            }

            var header = new GptHeader
            {
                Signature = GptSignature,
                Revision = ReadUInt32LittleEndian(headerBytes, 8),
                HeaderSize = ReadUInt32LittleEndian(headerBytes, 12),
                HeaderCrc32 = ReadUInt32LittleEndian(headerBytes, 16),
                Reserved = ReadUInt32LittleEndian(headerBytes, 20),
                CurrentLba = ReadUInt64LittleEndian(headerBytes, 24),
                BackupLba = ReadUInt64LittleEndian(headerBytes, 32),
                FirstUsableLba = ReadUInt64LittleEndian(headerBytes, 40),
                LastUsableLba = ReadUInt64LittleEndian(headerBytes, 48),
                DiskGuid = new Guid(CopyBytes(headerBytes, 56, 16)),
                PartitionEntriesLba = ReadUInt64LittleEndian(headerBytes, 72),
                PartitionsCount = ReadUInt32LittleEndian(headerBytes, 80),
                PartitionEntrySize = ReadUInt32LittleEndian(headerBytes, 84),
                PartitionEntryArrayCrc32 = ReadUInt32LittleEndian(headerBytes, 88)
            };

            if (header.HeaderSize < GptHeaderSize || header.HeaderSize > (uint)sectorSize)
            {
                return null;
            }

            if (header.PartitionsCount == 0 || header.PartitionEntrySize < 56)
            {
                return null;
            }

            uint headerSize = header.HeaderSize;
            var headerBuffer = new byte[headerSize];
            Buffer.BlockCopy(headerBytes, 0, headerBuffer, 0, (int)headerSize);
            Array.Clear(headerBuffer, 16, 4);
            bool headerCrcValid = Crc32.Compute(headerBuffer) == header.HeaderCrc32;

            if (!TryReadGptEntries(stream, header, sectorSize, out var entries, out var entryTableCrcValid))
            {
                return null;
            }

            return new GptPartitionTable(header, entries, mutable, sectorSize, headerCrcValid, entryTableCrcValid);
        }

        private static bool TryReadGptEntries(Stream stream, GptHeader header, int sectorSize, out List<GptPartitionEntry> entries, out bool entryTableCrcValid)
        {
            entries = new List<GptPartitionEntry>();
            entryTableCrcValid = false;

            if (header.PartitionsCount == 0)
            {
                entryTableCrcValid = header.PartitionEntryArrayCrc32 == 0;
                return true;
            }

            if (header.PartitionEntrySize > int.MaxValue)
            {
                return false;
            }

            int entrySize = (int)header.PartitionEntrySize;
            if ((ulong)header.PartitionsCount > (ulong)(int.MaxValue / entrySize))
            {
                return false;
            }

            int tableLength = checked((int)header.PartitionsCount * entrySize);
            var tableBytes = new byte[tableLength];
            long tableOffset = checked((long)header.PartitionEntriesLba * sectorSize);

            if (!TryReadBytes(stream, tableOffset, tableLength, tableBytes))
            {
                return false;
            }

            entryTableCrcValid = Crc32.Compute(tableBytes) == header.PartitionEntryArrayCrc32;

            int partitionCount = (int)header.PartitionsCount;
            for (int i = 0; i < partitionCount; i++)
            {
                int offset = i * entrySize;
                Guid partitionType = new Guid(CopyBytes(tableBytes, offset, 16));
                if (partitionType == Guid.Empty)
                {
                    continue;
                }

                entries.Add(new GptPartitionEntry
                {
                    PartitionType = partitionType,
                    PartitionId = new Guid(CopyBytes(tableBytes, offset + 16, 16)),
                    FirstLba = ReadUInt64LittleEndian(tableBytes, offset + 32),
                    LastLba = ReadUInt64LittleEndian(tableBytes, offset + 40),
                    Attributes = ReadUInt64LittleEndian(tableBytes, offset + 48),
                    Name = ReadUtf16String(tableBytes, offset + 56, Math.Min(72, entrySize - 56))
                });
            }

            return true;
        }

        private static bool TryReadBytes(Stream stream, long offset, int count, out byte[] buffer)
        {
            buffer = new byte[count];
            return TryReadBytes(stream, offset, count, buffer);
        }

        private static bool TryReadBytes(Stream stream, long offset, int count, byte[] buffer)
        {
            long originalPosition = stream.Position;
            try
            {
                stream.Position = offset;
                int totalRead = 0;
                while (totalRead < count)
                {
                    int read = stream.Read(buffer, totalRead, count - totalRead);
                    if (read == 0)
                    {
                        return false;
                    }

                    totalRead += read;
                }

                return true;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        internal static byte[] CopyBytes(byte[] source, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(source, offset, result, 0, length);
            return result;
        }

        internal static byte[] CloneBytes(byte[] source, int expectedLength)
        {
            var result = new byte[expectedLength];
            if (source != null)
            {
                Buffer.BlockCopy(source, 0, result, 0, Math.Min(expectedLength, source.Length));
            }

            return result;
        }

        internal static ushort ReadUInt16LittleEndian(byte[] source, int offset)
        {
            return (ushort)(source[offset] | (source[offset + 1] << 8));
        }

        internal static uint ReadUInt32LittleEndian(byte[] source, int offset)
        {
            return (uint)(source[offset]
                | (source[offset + 1] << 8)
                | (source[offset + 2] << 16)
                | (source[offset + 3] << 24));
        }

        internal static ulong ReadUInt64LittleEndian(byte[] source, int offset)
        {
            uint low = ReadUInt32LittleEndian(source, offset);
            uint high = ReadUInt32LittleEndian(source, offset + 4);
            return low | ((ulong)high << 32);
        }

        internal static string ReadUtf16String(byte[] source, int offset, int length)
        {
            if (length <= 0)
            {
                return string.Empty;
            }

            return Encoding.Unicode.GetString(source, offset, length).TrimEnd('\0');
        }

        internal static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        internal static void WriteUInt32LittleEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        internal static void WriteUInt64LittleEndian(byte[] buffer, int offset, ulong value)
        {
            WriteUInt32LittleEndian(buffer, offset, (uint)value);
            WriteUInt32LittleEndian(buffer, offset + 4, (uint)(value >> 32));
        }

        internal static byte[] BuildMbrImage(MbrPartitionEntry[] partitions, byte[] bootstrapCode)
        {
            var buffer = new byte[MbrSize];
            if (bootstrapCode != null)
            {
                Buffer.BlockCopy(bootstrapCode, 0, buffer, 0, Math.Min(bootstrapCode.Length, 446));
            }

            for (int i = 0; i < MbrPartitionCount; i++)
            {
                int offset = 446 + (i * 16);
                var partition = partitions[i];
                buffer[offset] = partition.Status;
                CopyPartitionChs(partition.FirstCHS, buffer, offset + 1);
                buffer[offset + 4] = partition.PartitionType;
                CopyPartitionChs(partition.LastCHS, buffer, offset + 5);
                WriteUInt32LittleEndian(buffer, offset + 8, partition.FirstLba);
                WriteUInt32LittleEndian(buffer, offset + 12, partition.SectorCount);
            }

            WriteUInt16LittleEndian(buffer, 510, MbrSignature);
            return buffer;
        }

        internal static void CopyPartitionChs(byte[] source, byte[] destination, int destinationOffset)
        {
            if (source == null)
            {
                return;
            }

            Buffer.BlockCopy(source, 0, destination, destinationOffset, Math.Min(3, source.Length));
        }

        internal static byte[] BuildGptHeader(GptHeader header, ulong currentLba, ulong backupLba, ulong partitionEntriesLba, uint partitionEntryArrayCrc32, int sectorSize)
        {
            var buffer = new byte[sectorSize];
            Encoding.ASCII.GetBytes(GptSignature).CopyTo(buffer, 0);
            WriteUInt32LittleEndian(buffer, 8, GptRevision);
            WriteUInt32LittleEndian(buffer, 12, GptHeaderSize);
            WriteUInt32LittleEndian(buffer, 16, 0);
            WriteUInt32LittleEndian(buffer, 20, 0);
            WriteUInt64LittleEndian(buffer, 24, currentLba);
            WriteUInt64LittleEndian(buffer, 32, backupLba);
            WriteUInt64LittleEndian(buffer, 40, header.FirstUsableLba);
            WriteUInt64LittleEndian(buffer, 48, header.LastUsableLba);
            header.DiskGuid.ToByteArray().CopyTo(buffer, 56);
            WriteUInt64LittleEndian(buffer, 72, partitionEntriesLba);
            WriteUInt32LittleEndian(buffer, 80, header.PartitionsCount);
            WriteUInt32LittleEndian(buffer, 84, header.PartitionEntrySize);
            WriteUInt32LittleEndian(buffer, 88, partitionEntryArrayCrc32);

            var headerPrefix = new byte[GptHeaderSize];
            Buffer.BlockCopy(buffer, 0, headerPrefix, 0, (int)GptHeaderSize);
            uint headerCrc32 = Crc32.ComputeWithExclusion(headerPrefix, 16, 4);
            WriteUInt32LittleEndian(buffer, 16, headerCrc32);
            return buffer;
        }

        internal static byte[] BuildGptEntryBuffer(IReadOnlyList<GptPartitionEntry> partitions, uint partitionCount, uint partitionEntrySize)
        {
            int entrySize = checked((int)partitionEntrySize);
            int tableLength = checked((int)partitionCount * entrySize);
            var buffer = new byte[tableLength];
            int limit = (int)partitionCount;

            for (int i = 0; i < limit; i++)
            {
                if (i >= partitions.Count)
                {
                    continue;
                }

                GptPartitionEntry entry = partitions[i];
                if (entry == null || entry.IsEmpty)
                {
                    continue;
                }

                int offset = i * entrySize;
                entry.PartitionType.ToByteArray().CopyTo(buffer, offset);
                entry.PartitionId.ToByteArray().CopyTo(buffer, offset + 16);
                WriteUInt64LittleEndian(buffer, offset + 32, entry.FirstLba);
                WriteUInt64LittleEndian(buffer, offset + 40, entry.LastLba);
                WriteUInt64LittleEndian(buffer, offset + 48, entry.Attributes);
                WritePartitionName(buffer, offset + 56, entrySize - 56, entry.Name);
            }

            return buffer;
        }

        internal static void WritePartitionName(byte[] buffer, int offset, int availableLength, string name)
        {
            if (availableLength <= 0)
            {
                return;
            }

            Array.Clear(buffer, offset, availableLength);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var nameBytes = Encoding.Unicode.GetBytes(name);
            int length = Math.Min(nameBytes.Length, availableLength);
            Buffer.BlockCopy(nameBytes, 0, buffer, offset, length);
        }

        internal static byte[] BuildProtectiveMbr()
        {
            var partitions = new MbrPartitionEntry[MbrPartitionCount];
            partitions[0] = new MbrPartitionEntry
            {
                Status = 0,
                PartitionType = 0xEE,
                FirstLba = 1,
                SectorCount = uint.MaxValue
            };

            return BuildMbrImage(partitions, Array.Empty<byte>());
        }

        internal static bool ContainsProtectivePartition(MbrPartitionEntry[] partitions)
        {
            for (int i = 0; i < partitions.Length; i++)
            {
                if (partitions[i].PartitionType == 0xEE)
                {
                    return true;
                }
            }

            return false;
        }

        internal static MbrPartitionEntry ClonePartitionEntry(MbrPartitionEntry entry)
        {
            if (entry == null)
            {
                return new MbrPartitionEntry();
            }

            return new MbrPartitionEntry
            {
                Status = entry.Status,
                FirstCHS = CloneBytes(entry.FirstCHS, 3),
                PartitionType = entry.PartitionType,
                LastCHS = CloneBytes(entry.LastCHS, 3),
                FirstLba = entry.FirstLba,
                SectorCount = entry.SectorCount
            };
        }

        internal static GptPartitionEntry ClonePartitionEntry(GptPartitionEntry entry)
        {
            if (entry == null)
            {
                return new GptPartitionEntry();
            }

            return new GptPartitionEntry
            {
                PartitionType = entry.PartitionType,
                PartitionId = entry.PartitionId,
                FirstLba = entry.FirstLba,
                LastLba = entry.LastLba,
                Attributes = entry.Attributes,
                Name = entry.Name ?? string.Empty
            };
        }

        internal struct MbrDescriptor
        {
            public byte[] BootstrapCode;
            public MbrPartitionEntry[] Partitions;
            public ushort Signature;
            public bool IsValid;
        }

        internal struct GptHeader
        {
            public string Signature;
            public uint Revision;
            public uint HeaderSize;
            public uint HeaderCrc32;
            public uint Reserved;
            public ulong CurrentLba;
            public ulong BackupLba;
            public ulong FirstUsableLba;
            public ulong LastUsableLba;
            public Guid DiskGuid;
            public ulong PartitionEntriesLba;
            public uint PartitionsCount;
            public uint PartitionEntrySize;
            public uint PartitionEntryArrayCrc32;
        }
    }
}
