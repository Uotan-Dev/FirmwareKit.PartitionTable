using System;
using System.Collections.Generic;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Portable manifest for partition table import/export.
    /// 分区表导入导出的可移植清单。
    /// </summary>
    public sealed class PartitionTableManifest
    {
        /// <summary>
        /// Gets or sets the partition table kind string.
        /// 获取或设置分区表类型字符串。
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the sector size used by the source image.
        /// 获取或设置源镜像使用的扇区大小。
        /// </summary>
        public int? SectorSize { get; set; }

        /// <summary>
        /// Gets or sets the disk GUID when the table is GPT.
        /// 获取或设置 GPT 表对应的磁盘 GUID。
        /// </summary>
        public Guid? DiskGuid { get; set; }

        /// <summary>
        /// Gets or sets the first usable LBA.
        /// 获取或设置第一个可用 LBA。
        /// </summary>
        public ulong? FirstUsableLba { get; set; }

        /// <summary>
        /// Gets or sets the last usable LBA.
        /// 获取或设置最后一个可用 LBA。
        /// </summary>
        public ulong? LastUsableLba { get; set; }

        /// <summary>
        /// Gets or sets GPT partition entries in the manifest.
        /// 获取或设置清单中的 GPT 分区项。
        /// </summary>
        public List<GptPartitionEntry> GptPartitions { get; set; } = new List<GptPartitionEntry>();

        /// <summary>
        /// Gets or sets MBR partition entries in the manifest.
        /// 获取或设置清单中的 MBR 分区项。
        /// </summary>
        public List<MbrPartitionEntry> MbrPartitions { get; set; } = new List<MbrPartitionEntry>();
    }
}
