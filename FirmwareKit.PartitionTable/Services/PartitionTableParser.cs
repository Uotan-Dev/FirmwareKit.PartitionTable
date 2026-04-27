using FirmwareKit.PartitionTable.Exceptions;
using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using FirmwareKit.PartitionTable.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Reads and writes MBR, GPT and Amlogic EPT partition tables.
    /// 读取和写入 MBR、GPT 与 Amlogic EPT 分区表。
    /// </summary>
    public static class PartitionTableParser
    {
        private const int MbrSize = 512;
        private const int MbrPartitionCount = 4;
        private const ushort MbrSignature = 0xAA55;
        private const uint AmlogicHeaderMagic = 0x0054504D;
        private const uint AmlogicVersionWord0 = 0x302E3130;
        private const uint AmlogicVersionWord1 = 0x30302E30;
        private const uint AmlogicVersionWord2 = 0x00000000;
        private const int AmlogicHeaderSize = 24;
        private const int AmlogicPartitionEntrySize = 40;
        internal const int AmlogicPartitionSlotCount = 32;
        private const int AmlogicTableSize = AmlogicHeaderSize + (AmlogicPartitionSlotCount * AmlogicPartitionEntrySize);
        private const string GptSignature = "EFI PART";
        private const uint GptRevision = 0x00010000;
        private const uint GptHeaderSize = 92;
        private static readonly int[] DefaultSectorSizes = { 512, 1024, 2048, 4096, 8192 };

        /// <summary>
        /// Reads a partition table from a seekable stream.
        /// 从可寻址流中读取分区表。
        /// </summary>
        /// <param name="stream">The source stream. / 源流。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <returns>The parsed partition table. / 解析后的分区表。</returns>
        public static IPartitionTable FromStream(Stream stream, bool mutable = false)
        {
            return FromStream(stream, mutable, (PartitionReadOptions?)null);
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
            return FromStream(stream, mutable, new PartitionReadOptions { PreferredSectorSize = sectorSize });
        }

        /// <summary>
        /// Reads a partition table from a seekable stream using advanced probing options.
        /// 使用高级探测选项从可寻址流中读取分区表。
        /// </summary>
        /// <param name="stream">The source stream. / 源流。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <param name="options">Read options. / 读取选项。</param>
        /// <returns>The parsed partition table. / 解析后的分区表。</returns>
        public static IPartitionTable FromStream(Stream stream, bool mutable, PartitionReadOptions? options)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new NotSupportedException("The stream must support seeking.");

            long originalPosition = stream.Position;
            try
            {
                var amlogic = TryReadAmlogicEpt(stream, mutable);
                if (amlogic != null)
                {
                    return amlogic;
                }

                var gpt = TryReadGpt(stream, mutable, options);
                if (gpt != null)
                {
                    return gpt;
                }

                if (TryReadMbr(stream, out var mbr))
                {
                    if (options != null
                        && options.StrictSectorSize
                        && options.PreferredSectorSize.HasValue
                        && ContainsProtectivePartition(mbr.Partitions))
                    {
                        throw new PartitionTableFormatException(
                            $"GPT table was not found with strict sector size {options.PreferredSectorSize.Value}.",
                            "GPT_STRICT_SECTOR_SIZE_NOT_FOUND",
                            PartitionTableType.Gpt);
                    }

                    return new MbrPartitionTable(mbr, mutable);
                }

                throw new PartitionTableFormatException(
                    "No valid Amlogic EPT, MBR, or GPT partition table could be identified from the stream.",
                    "FORMAT_NOT_RECOGNIZED");
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        internal static AmlogicPartitionTable? TryReadAmlogicEpt(Stream stream, bool mutable)
        {
            try
            {
                if (!TryReadBytes(stream, 0, AmlogicTableSize, out var tableBytes))
                {
                    return null;
                }

                uint magic = ReadUInt32LittleEndian(tableBytes, 0);
                if (magic != AmlogicHeaderMagic)
                {
                    return null;
                }

                uint version0 = ReadUInt32LittleEndian(tableBytes, 4);
                uint version1 = ReadUInt32LittleEndian(tableBytes, 8);
                uint version2 = ReadUInt32LittleEndian(tableBytes, 12);
                if (version0 != AmlogicVersionWord0 || version1 != AmlogicVersionWord1 || version2 != AmlogicVersionWord2)
                {
                    return null;
                }

                uint partitionsCount = ReadUInt32LittleEndian(tableBytes, 16);
                if (partitionsCount == 0 || partitionsCount > AmlogicPartitionTableSupport.PartitionSlotCount)
                {
                    return null;
                }

                uint recordedChecksum = ReadUInt32LittleEndian(tableBytes, 20);
                uint computedChecksum = ComputeAmlogicChecksum(tableBytes, (int)partitionsCount);

                var partitions = new List<AmlogicPartitionEntry>((int)partitionsCount);
                for (int i = 0; i < partitionsCount; i++)
                {
                    int offset = AmlogicHeaderSize + (i * AmlogicPartitionEntrySize);
                    string name = ReadAsciiString(tableBytes, offset, 16);
                    if (!AmlogicPartitionTableSupport.IsValidPartitionName(name))
                    {
                        return null;
                    }

                    partitions.Add(new AmlogicPartitionEntry
                    {
                        Name = name,
                        Size = ReadUInt64LittleEndian(tableBytes, offset + 16),
                        Offset = ReadUInt64LittleEndian(tableBytes, offset + 24),
                        MaskFlags = ReadUInt32LittleEndian(tableBytes, offset + 32)
                    });
                }

                return new AmlogicPartitionTable(
                    new[] { version0, version1, version2 },
                    recordedChecksum,
                    partitions,
                    mutable,
                    checksumValid: recordedChecksum == computedChecksum);
            }
            catch (OverflowException)
            {
                return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
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
            return FromFile(path, mutable, (PartitionReadOptions?)null);
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
            return FromFile(path, mutable, new PartitionReadOptions { PreferredSectorSize = sectorSize });
        }

        /// <summary>
        /// Reads a partition table from a file using advanced probing options.
        /// 使用高级探测选项从文件中读取分区表。
        /// </summary>
        /// <param name="path">The file path. / 文件路径。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <param name="options">Read options. / 读取选项。</param>
        /// <returns>The parsed partition table. / 解析后的分区表。</returns>
        public static IPartitionTable FromFile(string path, bool mutable, PartitionReadOptions? options)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var stream = File.OpenRead(path);
            return FromStream(stream, mutable, options);
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
                    && HasValidMbrPartitionStatus(partitions)
            };

            return descriptor.IsValid;
        }

        internal static MbrPartitionTable? TryReadMbrTable(Stream stream, bool mutable)
        {
            if (TryReadMbr(stream, out var descriptor) && descriptor.IsValid)
            {
                return new MbrPartitionTable(descriptor, mutable);
            }

            return null;
        }

        private static bool HasValidMbrPartitionStatus(MbrPartitionEntry[] partitions)
        {
            for (int i = 0; i < partitions.Length; i++)
            {
                byte status = partitions[i].Status;
                if (status != 0x00 && status != 0x80)
                {
                    return false;
                }
            }

            return true;
        }

        private static GptPartitionTable? TryReadGpt(Stream stream, bool mutable, PartitionReadOptions? options)
        {
            options ??= new PartitionReadOptions();
            int? preferredSectorSize = options.PreferredSectorSize;
            if (preferredSectorSize.HasValue)
            {
                if (preferredSectorSize.Value <= 0) throw new ArgumentOutOfRangeException(nameof(preferredSectorSize));

                var preferred = TryReadGptWithSectorSize(stream, mutable, preferredSectorSize.Value);
                if (preferred != null || options.StrictSectorSize)
                {
                    return preferred;
                }
            }

            IReadOnlyList<int> customProbe = options.GetProbeSectorSizes();
            for (int i = 0; i < customProbe.Count; i++)
            {
                int candidate = customProbe[i];
                if (candidate <= 0)
                {
                    continue;
                }

                if (preferredSectorSize.HasValue && candidate == preferredSectorSize.Value)
                {
                    continue;
                }

                var table = TryReadGptWithSectorSize(stream, mutable, candidate);
                if (table != null)
                {
                    return table;
                }
            }

            for (int i = 0; i < DefaultSectorSizes.Length; i++)
            {
                if (preferredSectorSize.HasValue && DefaultSectorSizes[i] == preferredSectorSize.Value)
                {
                    continue;
                }

                var table = TryReadGptWithSectorSize(stream, mutable, DefaultSectorSizes[i]);
                if (table != null)
                {
                    return table;
                }
            }

            return null;
        }

        internal static GptPartitionTable? TryReadGptWithSectorSize(Stream stream, bool mutable, int sectorSize)
        {
            try
            {
                if (!TryReadBytes(stream, sectorSize, sectorSize, out var headerBytes))
                {
                    return null;
                }

                if (!string.Equals(Encoding.ASCII.GetString(headerBytes, 0, 8), GptSignature, StringComparison.Ordinal))
                {
                    return null;
                }

                var header = ParseGptHeader(headerBytes, sectorSize);
                if (header == null)
                {
                    return null;
                }

                var gptHeader = header.Value;

                if (!TryReadGptEntries(stream, gptHeader, sectorSize, out var entries, out var entryTableCrcValid))
                {
                    return null;
                }

                bool headerCrcValid = ValidateGptHeaderCrc(headerBytes, gptHeader.HeaderSize, gptHeader.HeaderCrc32);

                if (!headerCrcValid || !entryTableCrcValid)
                {
                    var backupResult = TryReadGptFromBackup(stream, sectorSize, mutable);
                    if (backupResult != null)
                    {
                        return backupResult;
                    }
                }

                return new GptPartitionTable(gptHeader, entries, mutable, sectorSize, headerCrcValid, entryTableCrcValid);
            }
            catch (OverflowException)
            {
                return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static GptHeader? ParseGptHeader(byte[] headerBytes, int sectorSize)
        {
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

            return header;
        }

        private static bool ValidateGptHeaderCrc(byte[] headerBytes, uint headerSize, uint expectedCrc)
        {
            var headerBuffer = new byte[headerSize];
            Buffer.BlockCopy(headerBytes, 0, headerBuffer, 0, (int)headerSize);
            Array.Clear(headerBuffer, 16, 4);
            return Crc32.Compute(headerBuffer) == expectedCrc;
        }

        private static GptPartitionTable? TryReadGptFromBackup(Stream stream, int sectorSize, bool mutable)
        {
            try
            {
                if (stream.Length < (long)2 * sectorSize)
                {
                    return null;
                }

                long lastSectorOffset = stream.Length - sectorSize;
                if (!TryReadBytes(stream, lastSectorOffset, sectorSize, out var backupHeaderBytes))
                {
                    return null;
                }

                if (!string.Equals(Encoding.ASCII.GetString(backupHeaderBytes, 0, 8), GptSignature, StringComparison.Ordinal))
                {
                    return null;
                }

                var backupHeader = ParseGptHeader(backupHeaderBytes, sectorSize);
                if (backupHeader == null)
                {
                    return null;
                }

                var gptBackupHeader = backupHeader.Value;

                bool backupHeaderCrcValid = ValidateGptHeaderCrc(backupHeaderBytes, gptBackupHeader.HeaderSize, gptBackupHeader.HeaderCrc32);
                if (!backupHeaderCrcValid)
                {
                    return null;
                }

                if (!TryReadGptEntries(stream, gptBackupHeader, sectorSize, out var entries, out var entryTableCrcValid))
                {
                    return null;
                }

                return new GptPartitionTable(gptBackupHeader, entries, mutable, sectorSize, backupHeaderCrcValid, entryTableCrcValid, recoveredFromBackup: true);
            }
            catch (OverflowException)
            {
                return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
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

        internal static string ReadAsciiString(byte[] source, int offset, int length)
        {
            if (length <= 0)
            {
                return string.Empty;
            }

            int end = offset;
            int max = offset + length;
            while (end < max && source[end] != 0)
            {
                end++;
            }

            return Encoding.ASCII.GetString(source, offset, end - offset).Trim();
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
                var partition = partitions[i] ?? new MbrPartitionEntry();
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

        internal static byte[] BuildAmlogicEptImage(IReadOnlyList<uint>? versionWords, IReadOnlyList<AmlogicPartitionEntry> partitions)
        {
            var buffer = new byte[AmlogicTableSize];
            WriteUInt32LittleEndian(buffer, 0, AmlogicHeaderMagic);

            uint version0 = versionWords != null && versionWords.Count > 0 ? versionWords[0] : AmlogicVersionWord0;
            uint version1 = versionWords != null && versionWords.Count > 1 ? versionWords[1] : AmlogicVersionWord1;
            uint version2 = versionWords != null && versionWords.Count > 2 ? versionWords[2] : AmlogicVersionWord2;
            WriteUInt32LittleEndian(buffer, 4, version0);
            WriteUInt32LittleEndian(buffer, 8, version1);
            WriteUInt32LittleEndian(buffer, 12, version2);

            int count = Math.Min(partitions?.Count ?? 0, AmlogicPartitionSlotCount);
            WriteUInt32LittleEndian(buffer, 16, (uint)count);

            for (int i = 0; i < count; i++)
            {
                int offset = AmlogicHeaderSize + (i * AmlogicPartitionEntrySize);
                AmlogicPartitionEntry entry = partitions![i] ?? new AmlogicPartitionEntry();
                WriteAsciiName(buffer, offset, 16, entry.Name);
                WriteUInt64LittleEndian(buffer, offset + 16, entry.Size);
                WriteUInt64LittleEndian(buffer, offset + 24, entry.Offset);
                WriteUInt32LittleEndian(buffer, offset + 32, entry.MaskFlags);
                WriteUInt32LittleEndian(buffer, offset + 36, 0);
            }

            uint checksum = ComputeAmlogicChecksum(buffer, count);
            WriteUInt32LittleEndian(buffer, 20, checksum);
            return buffer;
        }

        internal static uint ComputeAmlogicChecksum(byte[] tableBytes, int partitionsCount)
        {
            if (tableBytes == null)
            {
                throw new ArgumentNullException(nameof(tableBytes));
            }

            int count = partitionsCount;
            if (count < 0)
            {
                count = 0;
            }

            if (count > AmlogicPartitionTableSupport.PartitionSlotCount)
            {
                count = AmlogicPartitionTableSupport.PartitionSlotCount;
            }

            uint checksum = 0;
            int firstPartitionOffset = AmlogicHeaderSize;
            int wordsPerPartition = AmlogicPartitionEntrySize / 4;
            for (int i = 0; i < count; i++)
            {
                int cursor = firstPartitionOffset;
                for (int j = 0; j < wordsPerPartition; j++)
                {
                    checksum += ReadUInt32LittleEndian(tableBytes, cursor);
                    cursor += 4;
                }
            }

            return checksum;
        }

        internal static void WriteAsciiName(byte[] buffer, int offset, int maxLength, string name)
        {
            Array.Clear(buffer, offset, maxLength);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            string normalized = name.Length > maxLength - 1 ? name.Substring(0, maxLength - 1) : name;
            byte[] bytes = Encoding.ASCII.GetBytes(normalized);
            Buffer.BlockCopy(bytes, 0, buffer, offset, Math.Min(bytes.Length, maxLength - 1));
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

        internal static AmlogicPartitionEntry ClonePartitionEntry(AmlogicPartitionEntry entry)
        {
            if (entry == null)
            {
                return new AmlogicPartitionEntry();
            }

            return new AmlogicPartitionEntry
            {
                Name = entry.Name ?? string.Empty,
                Size = entry.Size,
                Offset = entry.Offset,
                MaskFlags = entry.MaskFlags
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
