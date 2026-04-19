using System;

namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// Represents one GPT partition entry.
    /// 表示一个 GPT 分区项。
    /// </summary>
    public sealed class GptPartitionEntry
    {
        /// <summary>
        /// Gets or sets the partition type GUID.
        /// 获取或设置分区类型 GUID。
        /// </summary>
        public Guid PartitionType { get; set; }

        /// <summary>
        /// Gets or sets the unique partition GUID.
        /// 获取或设置分区唯一 GUID。
        /// </summary>
        public Guid PartitionId { get; set; }

        /// <summary>
        /// Gets or sets the first logical block address.
        /// 获取或设置首个逻辑块地址。
        /// </summary>
        public ulong FirstLba { get; set; }

        /// <summary>
        /// Gets or sets the last logical block address.
        /// 获取或设置末尾逻辑块地址。
        /// </summary>
        public ulong LastLba { get; set; }

        /// <summary>
        /// Gets or sets the attribute flags.
        /// 获取或设置属性标志。
        /// </summary>
        public ulong Attributes { get; set; }

        /// <summary>
        /// Gets or sets the human-readable partition name.
        /// 获取或设置可读的分区名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets a value indicating whether the entry is empty.
        /// 获取该分区项是否为空。
        /// </summary>
        public bool IsEmpty => PartitionType == Guid.Empty;
    }
}
