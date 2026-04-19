namespace FirmwareKit.PartitionTable.Models
{
    /// <summary>
    /// The supported partition table kinds.
    /// 支持的分区表类型。
    /// </summary>
    public enum PartitionTableType
    {
        /// <summary>
        /// Master Boot Record partition table.
        /// 主引导记录分区表。
        /// </summary>
        Mbr,

        /// <summary>
        /// GUID Partition Table.
        /// GUID 分区表。
        /// </summary>
        Gpt
    }
}
