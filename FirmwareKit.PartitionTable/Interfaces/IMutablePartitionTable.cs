namespace FirmwareKit.PartitionTable.Interfaces
{
    /// <summary>
    /// Adds mutability control to a partition table.
    /// 为分区表增加可变性控制。
    /// </summary>
    public interface IMutablePartitionTable : IPartitionTable
    {
        /// <summary>
        /// Sets whether the table can be edited.
        /// 设置当前表是否可编辑。
        /// </summary>
        /// <param name="mutable">Whether the table should be mutable.</param>
        void SetMutable(bool mutable);
    }
}
