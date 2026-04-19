using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using System;
using System.Collections.Generic;
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
    }
}
