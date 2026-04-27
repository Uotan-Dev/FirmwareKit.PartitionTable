using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using FirmwareKit.PartitionTable.Services;
using FirmwareKit.PartitionTable.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FirmwareKit.PartitionTable.Json.Services
{
    /// <summary>
    /// Provides JSON manifest import/export for partition tables.
    /// 提供分区表的 JSON 清单导入/导出功能。
    /// </summary>
    public static class PartitionTableManifestSerializer
    {
        /// <summary>
        /// Exports a partition table to a JSON manifest string.
        /// 将分区表导出为 JSON 清单字符串。
        /// </summary>
        /// <param name="table">The partition table to export. / 待导出的分区表。</param>
        /// <param name="indented">Whether to indent the JSON output. / 是否缩进 JSON 输出。</param>
        /// <returns>The JSON manifest string. / JSON 清单字符串。</returns>
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
        /// Exports a partition table to a JSON manifest file.
        /// 将分区表导出为 JSON 清单文件。
        /// </summary>
        /// <param name="table">The partition table to export. / 待导出的分区表。</param>
        /// <param name="path">The output file path. / 输出文件路径。</param>
        /// <param name="indented">Whether to indent the JSON output. / 是否缩进 JSON 输出。</param>
        public static void ExportToFile(IPartitionTable table, string path, bool indented = true)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (path == null) throw new ArgumentNullException(nameof(path));

            File.WriteAllText(path, ExportToJson(table, indented));
        }

        /// <summary>
        /// Imports a partition table manifest from a JSON string.
        /// 从 JSON 字符串导入分区表清单。
        /// </summary>
        /// <param name="json">The JSON manifest string. / JSON 清单字符串。</param>
        /// <returns>The deserialized manifest. / 反序列化的清单。</returns>
        public static PartitionTableManifest ImportFromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            var manifest = JsonSerializer.Deserialize<PartitionTableManifest>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException("Failed to deserialize partition table manifest.");
            }

            if (manifest.SchemaVersion > PartitionTableManifest.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Manifest schema version {manifest.SchemaVersion} is not supported. The maximum supported version is {PartitionTableManifest.CurrentSchemaVersion}.");
            }

            return manifest;
        }

        /// <summary>
        /// Imports a partition table manifest from a JSON file.
        /// 从 JSON 文件导入分区表清单。
        /// </summary>
        /// <param name="path">The input file path. / 输入文件路径。</param>
        /// <returns>The deserialized manifest. / 反序列化的清单。</returns>
        public static PartitionTableManifest ImportFromFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            return ImportFromJson(File.ReadAllText(path));
        }

        /// <summary>
        /// Reconstructs a partition table from a manifest object.
        /// 从清单对象重建分区表。
        /// </summary>
        /// <param name="manifest">The manifest to reconstruct from. / 用于重建的清单。</param>
        /// <param name="mutable">Whether the resulting table should be mutable. / 结果表是否可变。</param>
        /// <returns>The reconstructed partition table. / 重建的分区表。</returns>
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
        /// Reconstructs a partition table from a JSON manifest string.
        /// 从 JSON 清单字符串重建分区表。
        /// </summary>
        /// <param name="json">The JSON manifest string. / JSON 清单字符串。</param>
        /// <param name="mutable">Whether the resulting table should be mutable. / 结果表是否可变。</param>
        /// <returns>The reconstructed partition table. / 重建的分区表。</returns>
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
            var entries = new MbrPartitionEntry[4];
            for (int i = 0; i < 4; i++)
            {
                entries[i] = i < partitions.Count && partitions[i] != null ? partitions[i] : new MbrPartitionEntry();
            }

            return PartitionTableParser.BuildMbrImage(entries, null!);
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

            var entryBuffer = PartitionTableParser.BuildGptEntryBuffer(manifest.GptPartitions, partitionCount, partitionEntrySize);
            uint entryBufferCrc = Crc32.Compute(entryBuffer);

            var buffer = new byte[checked((int)(totalSectors * (ulong)sectorSize))];
            byte[] protectiveMbr = PartitionTableParser.BuildProtectiveMbr();
            Buffer.BlockCopy(protectiveMbr, 0, buffer, 0, Math.Min(protectiveMbr.Length, buffer.Length));

            Guid diskGuid = manifest.DiskGuid ?? Guid.NewGuid();
            var header = new PartitionTableParser.GptHeader
            {
                FirstUsableLba = firstUsableLba,
                LastUsableLba = lastUsableLba,
                DiskGuid = diskGuid,
                PartitionsCount = partitionCount,
                PartitionEntrySize = partitionEntrySize
            };

            byte[] primaryHeader = PartitionTableParser.BuildGptHeader(header, currentLba: 1, backupLba: backupLba, partitionEntriesLba: 2, partitionEntryArrayCrc32: entryBufferCrc, sectorSize: sectorSize);
            Buffer.BlockCopy(primaryHeader, 0, buffer, sectorSize, primaryHeader.Length);
            Buffer.BlockCopy(entryBuffer, 0, buffer, checked((int)(2 * (ulong)sectorSize)), entryBuffer.Length);
            Buffer.BlockCopy(entryBuffer, 0, buffer, checked((int)(backupEntriesLba * (ulong)sectorSize)), entryBuffer.Length);

            byte[] backupHeader = PartitionTableParser.BuildGptHeader(header, currentLba: backupLba, backupLba: 1, partitionEntriesLba: backupEntriesLba, partitionEntryArrayCrc32: entryBufferCrc, sectorSize: sectorSize);
            Buffer.BlockCopy(backupHeader, 0, buffer, checked((int)(backupLba * (ulong)sectorSize)), backupHeader.Length);
            return buffer;
        }

        private static byte[] BuildAmlogicImage(PartitionTableManifest manifest)
        {
            IReadOnlyList<AmlogicPartitionEntry> partitions = manifest.AmlogicPartitions ?? new List<AmlogicPartitionEntry>();
            return PartitionTableParser.BuildAmlogicEptImage(null, partitions);
        }
    }
}
