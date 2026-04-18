using System.IO;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Common partition table operations.
    /// 分区表通用操作。
    /// </summary>
    public interface IPartitionTable
    {
        /// <summary>
        /// Gets the partition table kind.
        /// 获取分区表类型。
        /// </summary>
        PartitionTableType Type { get; }

        /// <summary>
        /// Gets a value indicating whether the table can be edited.
        /// 获取当前表是否可编辑。
        /// </summary>
        bool IsMutable { get; }

        /// <summary>
        /// Ensures the table is mutable before editing.
        /// 在编辑前确保当前表可写。
        /// </summary>
        void EnsureMutable();

        /// <summary>
        /// Writes the table back to a stream.
        /// 将当前表写回到流。
        /// </summary>
        /// <param name="stream">The destination stream.</param>
        void WriteToStream(Stream stream);

        /// <summary>
        /// Writes the table back to a file.
        /// 将当前表写回到文件。
        /// </summary>
        /// <param name="path">The destination path.</param>
        void WriteToFile(string path);
    }
}
