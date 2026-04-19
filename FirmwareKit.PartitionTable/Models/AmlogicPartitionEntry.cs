namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// Represents one Amlogic EPT partition entry.
    /// 表示一个 Amlogic EPT 分区项。
    /// </summary>
    public sealed class AmlogicPartitionEntry
    {
        /// <summary>
        /// Gets or sets the partition name (up to 15 ASCII characters).
        /// 获取或设置分区名称（最多 15 个 ASCII 字符）。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the partition size in bytes.
        /// 获取或设置分区大小（字节）。
        /// </summary>
        public ulong Size { get; set; }

        /// <summary>
        /// Gets or sets the partition offset in bytes.
        /// 获取或设置分区偏移（字节）。
        /// </summary>
        public ulong Offset { get; set; }

        /// <summary>
        /// Gets or sets the partition mask flags.
        /// 获取或设置分区掩码标志。
        /// </summary>
        public uint MaskFlags { get; set; }

        /// <summary>
        /// Gets a value indicating whether the entry is empty.
        /// 获取该分区项是否为空。
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(Name) && Size == 0 && Offset == 0 && MaskFlags == 0;
    }
}
