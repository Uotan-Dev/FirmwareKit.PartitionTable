using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using FirmwareKit.PartitionTable.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Serializes partition tables to/from a portable JSON manifest.
    /// 将分区表序列化/反序列化为可移植 JSON 清单。
    /// </summary>
    public static class PartitionTableManifestSerializer
    {
        private const uint AmlogicHeaderMagic = 0x0054504D;
        private const uint AmlogicVersionWord0 = 0x302E3130;
        private const uint AmlogicVersionWord1 = 0x30302E30;
        private const uint AmlogicVersionWord2 = 0x00000000;
        private const int AmlogicHeaderSize = 24;
        private const int AmlogicPartitionEntrySize = 40;
        private const int AmlogicPartitionSlotCount = 32;

        /// <summary>
        /// Exports a partition table to JSON manifest text.
        /// 将分区表导出为 JSON 清单文本。
        /// </summary>
        /// <param name="table">The partition table to export. / 待导出的分区表。</param>
        /// <param name="indented">Whether to pretty-print JSON. / 是否格式化输出 JSON。</param>
        /// <returns>Serialized JSON manifest. / 序列化后的 JSON 清单。</returns>
        public static string ExportToJson(IPartitionTable table, bool indented = true)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var manifest = new PartitionTableManifest
            {
                Kind = table.Type.ToString()
            };

            if (table is GptPartitionTable gpt)
            {
                manifest.SectorSize = gpt.SectorSize;
                manifest.DiskGuid = gpt.DiskGuid;
                manifest.FirstUsableLba = gpt.FirstUsableLba;
                manifest.LastUsableLba = gpt.LastUsableLba;
                manifest.GptPartitions = new List<GptPartitionEntry>(gpt.Partitions);
            }
            else if (table is MbrPartitionTable mbr)
            {
                manifest.MbrPartitions = new List<MbrPartitionEntry>(mbr.Partitions);
            }
            else if (table is AmlogicPartitionTable amlogic)
            {
                manifest.AmlogicPartitions = new List<AmlogicPartitionEntry>(amlogic.Partitions);
            }

            var options = new JsonSerializerOptions { WriteIndented = indented };
            return JsonSerializer.Serialize(manifest, options);
        }

        /// <summary>
        /// Exports a partition table to a JSON file.
        /// 将分区表导出到 JSON 文件。
        /// </summary>
        /// <param name="table">The partition table to export. / 待导出的分区表。</param>
        /// <param name="path">The destination JSON file path. / 目标 JSON 文件路径。</param>
        /// <param name="indented">Whether to pretty-print JSON. / 是否格式化输出 JSON。</param>
        public static void ExportToFile(IPartitionTable table, string path, bool indented = true)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (path == null) throw new ArgumentNullException(nameof(path));

            File.WriteAllText(path, ExportToJson(table, indented));
        }

        /// <summary>
        /// Imports a JSON manifest into a portable partition manifest model.
        /// 将 JSON 清单导入为可移植分区清单模型。
        /// </summary>
        /// <param name="json">The JSON manifest text. / JSON 清单文本。</param>
        /// <returns>The parsed manifest model. / 解析后的清单模型。</returns>
        public static PartitionTableManifest ImportFromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            var manifest = JsonSerializer.Deserialize<PartitionTableManifest>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException("Failed to deserialize partition table manifest.");
            }

            return manifest;
        }

        /// <summary>
        /// Imports a JSON manifest from a file.
        /// 从文件导入 JSON 清单。
        /// </summary>
        /// <param name="path">The manifest file path. / 清单文件路径。</param>
        /// <returns>The parsed manifest model. / 解析后的清单模型。</returns>
        public static PartitionTableManifest ImportFromFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            return ImportFromJson(File.ReadAllText(path));
        }

        /// <summary>
        /// Creates a mutable partition table from a manifest.
        /// 根据清单创建可变分区表。
        /// </summary>
        /// <param name="manifest">The source manifest. / 源清单。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <returns>The materialized table. / 生成的分区表。</returns>
        public static IPartitionTable ToPartitionTable(PartitionTableManifest manifest, bool mutable = true)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            string kind = manifest.Kind?.Trim() ?? string.Empty;
            if (string.Equals(kind, PartitionTableType.Mbr.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return ParseTable(BuildMbrImage(manifest.MbrPartitions), mutable);
            }

            if (string.Equals(kind, PartitionTableType.Gpt.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return ParseTable(BuildGptImage(manifest), mutable, manifest.SectorSize.GetValueOrDefault(512));
            }

            if (string.Equals(kind, PartitionTableType.AmlogicEpt.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return ParseTable(BuildAmlogicImage(manifest), mutable);
            }

            throw new InvalidOperationException($"Unsupported manifest kind: {manifest.Kind}");
        }

        /// <summary>
        /// Creates a partition table from a manifest JSON payload.
        /// 根据清单 JSON 生成分区表。
        /// </summary>
        /// <param name="json">The manifest JSON. / 清单 JSON。</param>
        /// <param name="mutable">Whether the returned table should be editable. / 返回的表是否可编辑。</param>
        /// <returns>The materialized table. / 生成的分区表。</returns>
        public static IPartitionTable ToPartitionTable(string json, bool mutable = true)
        {
            return ToPartitionTable(ImportFromJson(json), mutable);
        }

        private static IPartitionTable ParseTable(byte[] image, bool mutable, int? sectorSize = null)
        {
            using var stream = new MemoryStream(image, writable: false);
            return sectorSize.HasValue
                ? PartitionTableReader.FromStream(stream, mutable, sectorSize)
                : PartitionTableReader.FromStream(stream, mutable);
        }

        private static byte[] BuildMbrImage(IReadOnlyList<MbrPartitionEntry> partitions)
        {
            var buffer = new byte[512];
            for (int i = 0; i < 4; i++)
            {
                int offset = 446 + (i * 16);
                MbrPartitionEntry partition = i < partitions.Count && partitions[i] != null ? partitions[i] : new MbrPartitionEntry();
                buffer[offset] = partition.Status;
                CopyBytes(partition.FirstCHS, buffer, offset + 1, 3);
                buffer[offset + 4] = partition.PartitionType;
                CopyBytes(partition.LastCHS, buffer, offset + 5, 3);
                WriteUInt32LittleEndian(buffer, offset + 8, partition.FirstLba);
                WriteUInt32LittleEndian(buffer, offset + 12, partition.SectorCount);
            }

            WriteUInt16LittleEndian(buffer, 510, 0xAA55);
            return buffer;
        }

        private static byte[] BuildGptImage(PartitionTableManifest manifest)
        {
            int sectorSize = manifest.SectorSize.GetValueOrDefault(512);
            uint partitionCount = (uint)Math.Max(manifest.GptPartitions.Count, 128);
            uint partitionEntrySize = 128;
            ulong entryTableSectors = ((ulong)partitionCount * partitionEntrySize + (ulong)sectorSize - 1) / (ulong)sectorSize;
            ulong minimumTotalSectors = 3 + (entryTableSectors * 2);
            ulong totalSectors = Math.Max(128UL, minimumTotalSectors);
            ulong backupLba = totalSectors - 1;
            ulong backupEntriesLba = backupLba - entryTableSectors;
            ulong firstUsableLba = manifest.FirstUsableLba.GetValueOrDefault(34);
            ulong lastUsableLba = manifest.LastUsableLba.GetValueOrDefault(backupEntriesLba > 0 ? backupEntriesLba - 1 : 0);
            if (lastUsableLba >= backupEntriesLba)
            {
                lastUsableLba = backupEntriesLba > 0 ? backupEntriesLba - 1 : 0;
            }

            var entryBuffer = BuildGptEntryBuffer(manifest.GptPartitions, partitionCount, partitionEntrySize);
            uint entryBufferCrc = Crc32.Compute(entryBuffer);

            var buffer = new byte[checked((int)(totalSectors * (ulong)sectorSize))];
            CopyBytes(BuildProtectiveMbr(), buffer, 0, 512);

            Guid diskGuid = manifest.DiskGuid ?? Guid.NewGuid();
            byte[] primaryHeader = BuildGptHeader(
                firstUsableLba,
                lastUsableLba,
                diskGuid,
                currentLba: 1,
                backupLba: backupLba,
                partitionEntriesLba: 2,
                partitionCount: partitionCount,
                partitionEntrySize: partitionEntrySize,
                partitionEntryArrayCrc32: entryBufferCrc,
                sectorSize: sectorSize);

            CopyBytes(primaryHeader, buffer, sectorSize, primaryHeader.Length);
            CopyBytes(entryBuffer, buffer, checked((int)(2 * (ulong)sectorSize)), entryBuffer.Length);
            CopyBytes(entryBuffer, buffer, checked((int)(backupEntriesLba * (ulong)sectorSize)), entryBuffer.Length);

            byte[] backupHeader = BuildGptHeader(
                firstUsableLba,
                lastUsableLba,
                diskGuid,
                currentLba: backupLba,
                backupLba: 1,
                partitionEntriesLba: backupEntriesLba,
                partitionCount: partitionCount,
                partitionEntrySize: partitionEntrySize,
                partitionEntryArrayCrc32: entryBufferCrc,
                sectorSize: sectorSize);

            CopyBytes(backupHeader, buffer, checked((int)(backupLba * (ulong)sectorSize)), backupHeader.Length);
            return buffer;
        }

        private static byte[] BuildAmlogicImage(PartitionTableManifest manifest)
        {
            IReadOnlyList<AmlogicPartitionEntry> partitions = manifest.AmlogicPartitions ?? new List<AmlogicPartitionEntry>();
            int count = Math.Min(partitions.Count, AmlogicPartitionSlotCount);
            var buffer = new byte[AmlogicHeaderSize + (AmlogicPartitionSlotCount * AmlogicPartitionEntrySize)];

            WriteUInt32LittleEndian(buffer, 0, AmlogicHeaderMagic);
            WriteUInt32LittleEndian(buffer, 4, AmlogicVersionWord0);
            WriteUInt32LittleEndian(buffer, 8, AmlogicVersionWord1);
            WriteUInt32LittleEndian(buffer, 12, AmlogicVersionWord2);
            WriteUInt32LittleEndian(buffer, 16, (uint)count);
            WriteUInt32LittleEndian(buffer, 20, 0);

            for (int i = 0; i < count; i++)
            {
                AmlogicPartitionEntry entry = partitions[i] ?? new AmlogicPartitionEntry();
                int offset = AmlogicHeaderSize + (i * AmlogicPartitionEntrySize);
                WriteAsciiName(buffer, offset, 16, entry.Name);
                WriteUInt64LittleEndian(buffer, offset + 16, entry.Size);
                WriteUInt64LittleEndian(buffer, offset + 24, entry.Offset);
                WriteUInt32LittleEndian(buffer, offset + 32, entry.MaskFlags);
                WriteUInt32LittleEndian(buffer, offset + 36, 0);
            }

            WriteUInt32LittleEndian(buffer, 20, ComputeAmlogicChecksum(buffer, count));
            return buffer;
        }

        private static byte[] BuildProtectiveMbr()
        {
            var buffer = new byte[512];
            buffer[446 + 4] = 0xEE;
            WriteUInt32LittleEndian(buffer, 446 + 8, 1);
            WriteUInt32LittleEndian(buffer, 446 + 12, uint.MaxValue);
            WriteUInt16LittleEndian(buffer, 510, 0xAA55);
            return buffer;
        }

        private static byte[] BuildGptEntryBuffer(IReadOnlyList<GptPartitionEntry> partitions, uint partitionCount, uint partitionEntrySize)
        {
            int entrySize = checked((int)partitionEntrySize);
            int tableLength = checked((int)partitionCount * entrySize);
            var buffer = new byte[tableLength];

            for (int i = 0; i < partitionCount; i++)
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
                CopyBytes(entry.PartitionType.ToByteArray(), buffer, offset, 16);
                CopyBytes(entry.PartitionId.ToByteArray(), buffer, offset + 16, 16);
                WriteUInt64LittleEndian(buffer, offset + 32, entry.FirstLba);
                WriteUInt64LittleEndian(buffer, offset + 40, entry.LastLba);
                WriteUInt64LittleEndian(buffer, offset + 48, entry.Attributes);
                WritePartitionName(buffer, offset + 56, entrySize - 56, entry.Name);
            }

            return buffer;
        }

        private static byte[] BuildGptHeader(
            ulong firstUsableLba,
            ulong lastUsableLba,
            Guid diskGuid,
            ulong currentLba,
            ulong backupLba,
            ulong partitionEntriesLba,
            uint partitionCount,
            uint partitionEntrySize,
            uint partitionEntryArrayCrc32,
            int sectorSize)
        {
            var buffer = new byte[sectorSize];
            Encoding.ASCII.GetBytes("EFI PART").CopyTo(buffer, 0);
            WriteUInt32LittleEndian(buffer, 8, 0x00010000);
            WriteUInt32LittleEndian(buffer, 12, 92);
            WriteUInt32LittleEndian(buffer, 16, 0);
            WriteUInt32LittleEndian(buffer, 20, 0);
            WriteUInt64LittleEndian(buffer, 24, currentLba);
            WriteUInt64LittleEndian(buffer, 32, backupLba);
            WriteUInt64LittleEndian(buffer, 40, firstUsableLba);
            WriteUInt64LittleEndian(buffer, 48, lastUsableLba);
            diskGuid.ToByteArray().CopyTo(buffer, 56);
            WriteUInt64LittleEndian(buffer, 72, partitionEntriesLba);
            WriteUInt32LittleEndian(buffer, 80, partitionCount);
            WriteUInt32LittleEndian(buffer, 84, partitionEntrySize);
            WriteUInt32LittleEndian(buffer, 88, partitionEntryArrayCrc32);
            var headerPrefix = new byte[92];
            Buffer.BlockCopy(buffer, 0, headerPrefix, 0, 92);
            WriteUInt32LittleEndian(buffer, 16, Crc32.ComputeWithExclusion(headerPrefix, 16, 4));
            return buffer;
        }

        private static uint ComputeAmlogicChecksum(byte[] tableBytes, int partitionsCount)
        {
            int count = Math.Min(Math.Max(partitionsCount, 0), AmlogicPartitionSlotCount);
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

        private static void WritePartitionName(byte[] buffer, int offset, int availableLength, string? name)
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

            byte[] nameBytes = Encoding.Unicode.GetBytes(name);
            int length = Math.Min(nameBytes.Length, availableLength);
            Buffer.BlockCopy(nameBytes, 0, buffer, offset, length);
        }

        private static void WriteAsciiName(byte[] buffer, int offset, int maxLength, string? name)
        {
            Array.Clear(buffer, offset, maxLength);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            string normalized = name ?? string.Empty;
            if (normalized.Length > maxLength - 1)
            {
                normalized = normalized.Substring(0, maxLength - 1);
            }

            byte[] bytes = Encoding.ASCII.GetBytes(normalized);
            Buffer.BlockCopy(bytes, 0, buffer, offset, Math.Min(bytes.Length, maxLength - 1));
        }

        private static void CopyBytes(byte[] source, byte[] destination, int destinationOffset, int length)
        {
            if (source == null)
            {
                return;
            }

            Buffer.BlockCopy(source, 0, destination, destinationOffset, Math.Min(length, source.Length));
        }

        private static ushort ReadUInt16LittleEndian(byte[] source, int offset)
        {
            return (ushort)(source[offset] | (source[offset + 1] << 8));
        }

        private static uint ReadUInt32LittleEndian(byte[] source, int offset)
        {
            return (uint)(source[offset]
                | (source[offset + 1] << 8)
                | (source[offset + 2] << 16)
                | (source[offset + 3] << 24));
        }

        private static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32LittleEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64LittleEndian(byte[] buffer, int offset, ulong value)
        {
            WriteUInt32LittleEndian(buffer, offset, (uint)value);
            WriteUInt32LittleEndian(buffer, offset + 4, (uint)(value >> 32));
        }
    }
}