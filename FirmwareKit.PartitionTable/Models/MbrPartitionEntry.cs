namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Represents one MBR partition entry.
    /// 表示一个 MBR 分区项。
    /// </summary>
    public sealed class MbrPartitionEntry
    {
        /// <summary>
        /// Gets or sets the status byte.
        /// 获取或设置状态字节。
        /// </summary>
        public byte Status { get; set; }

        /// <summary>
        /// Gets or sets the first CHS address.
        /// 获取或设置首个 CHS 地址。
        /// </summary>
        public byte[] FirstCHS { get; set; } = new byte[3];

        /// <summary>
        /// Gets or sets the partition type identifier.
        /// 获取或设置分区类型标识。
        /// </summary>
        public byte PartitionType { get; set; }

        /// <summary>
        /// Gets or sets the last CHS address.
        /// 获取或设置末尾 CHS 地址。
        /// </summary>
        public byte[] LastCHS { get; set; } = new byte[3];

        /// <summary>
        /// Gets or sets the first logical block address.
        /// 获取或设置首个逻辑块地址。
        /// </summary>
        public uint FirstLba { get; set; }

        /// <summary>
        /// Gets or sets the partition size in sectors.
        /// 获取或设置分区大小（以扇区计）。
        /// </summary>
        public uint SectorCount { get; set; }

        /// <summary>
        /// Gets a value indicating whether the entry is empty.
        /// 获取该分区项是否为空。
        /// </summary>
        public bool IsEmpty => PartitionType == 0 && Status == 0 && FirstLba == 0 && SectorCount == 0;
    }
}
