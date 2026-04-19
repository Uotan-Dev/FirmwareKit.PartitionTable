using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Serializes partition tables to/from a portable JSON manifest.
    /// 将分区表序列化/反序列化为可移植 JSON 清单。
    /// </summary>
    public static class PartitionTableManifestSerializer
    {
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
                return CreateMbrTable(manifest, mutable);
            }

            if (string.Equals(kind, PartitionTableType.Gpt.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return CreateGptTable(manifest, mutable);
            }

            if (string.Equals(kind, PartitionTableType.AmlogicEpt.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return CreateAmlogicTable(manifest, mutable);
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

        private static IPartitionTable CreateMbrTable(PartitionTableManifest manifest, bool mutable)
        {
            var partitions = new MbrPartitionEntry[4];
            for (int i = 0; i < partitions.Length && i < manifest.MbrPartitions.Count; i++)
            {
                partitions[i] = PartitionTableParser.ClonePartitionEntry(manifest.MbrPartitions[i]);
            }

            var descriptor = new PartitionTableParser.MbrDescriptor
            {
                BootstrapCode = Array.Empty<byte>(),
                Partitions = partitions,
                Signature = 0xAA55,
                IsValid = true
            };

            return new MbrPartitionTable(descriptor, mutable);
        }

        private static IPartitionTable CreateGptTable(PartitionTableManifest manifest, bool mutable)
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

            var entries = new List<GptPartitionEntry>(manifest.GptPartitions.Count);
            for (int i = 0; i < manifest.GptPartitions.Count; i++)
            {
                entries.Add(PartitionTableParser.ClonePartitionEntry(manifest.GptPartitions[i]));
            }

            var header = new PartitionTableParser.GptHeader
            {
                Signature = "EFI PART",
                Revision = 0x00010000,
                HeaderSize = 92,
                HeaderCrc32 = 0,
                Reserved = 0,
                CurrentLba = 1,
                BackupLba = backupLba,
                FirstUsableLba = firstUsableLba,
                LastUsableLba = lastUsableLba,
                DiskGuid = manifest.DiskGuid ?? Guid.NewGuid(),
                PartitionEntriesLba = 2,
                PartitionsCount = partitionCount,
                PartitionEntrySize = partitionEntrySize,
                PartitionEntryArrayCrc32 = 0
            };

            byte[] entryBuffer = PartitionTableParser.BuildGptEntryBuffer(entries, partitionCount, partitionEntrySize);
            uint entryBufferCrc = FirmwareKit.PartitionTable.Util.Crc32.Compute(entryBuffer);
            byte[] headerBuffer = PartitionTableParser.BuildGptHeader(header, header.CurrentLba, header.BackupLba, header.PartitionEntriesLba, entryBufferCrc, sectorSize);
            uint headerCrc = BitConverter.ToUInt32(headerBuffer, 16);
            header.HeaderCrc32 = headerCrc;
            header.PartitionEntryArrayCrc32 = entryBufferCrc;

            return new GptPartitionTable(header, entries, mutable, sectorSize, headerCrcValid: true, entryTableCrcValid: true);
        }

        private static IPartitionTable CreateAmlogicTable(PartitionTableManifest manifest, bool mutable)
        {
            var partitions = new List<AmlogicPartitionEntry>(manifest.AmlogicPartitions.Count);
            for (int i = 0; i < manifest.AmlogicPartitions.Count; i++)
            {
                partitions.Add(PartitionTableParser.ClonePartitionEntry(manifest.AmlogicPartitions[i]));
            }

            byte[] image = PartitionTableParser.BuildAmlogicEptImage(null, partitions);
            using var stream = new MemoryStream(image, writable: false);
            return PartitionTableReader.FromStream(stream, mutable: mutable);
        }
    }
}
