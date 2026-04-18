using System.IO;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Convenience entry points for partition table parsing.
    /// 分区表解析的便捷入口。
    /// </summary>
    public static class PartitionTableReader
    {
        /// <summary>
        /// Parses a partition table from a seekable stream.
        /// 从可寻址流中解析分区表。
        /// </summary>
        /// <param name="stream">The source stream.</param>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <returns>The parsed partition table.</returns>
        public static IPartitionTable FromStream(Stream stream, bool mutable = false)
        {
            return PartitionTableParser.FromStream(stream, mutable);
        }

        /// <summary>
        /// Parses a partition table from a seekable stream using a preferred sector size.
        /// 使用首选扇区大小从可寻址流中解析分区表。
        /// </summary>
        /// <param name="stream">The source stream.</param>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <param name="sectorSize">The sector size to prefer, or <see langword="null" /> for auto-detection.</param>
        /// <returns>The parsed partition table.</returns>
        public static IPartitionTable FromStream(Stream stream, bool mutable, int? sectorSize)
        {
            return PartitionTableParser.FromStream(stream, mutable, sectorSize);
        }

        /// <summary>
        /// Parses a partition table from a file.
        /// 从文件中解析分区表。
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <returns>The parsed partition table.</returns>
        public static IPartitionTable FromFile(string path, bool mutable = false)
        {
            return PartitionTableParser.FromFile(path, mutable);
        }

        /// <summary>
        /// Parses a partition table from a file using a preferred sector size.
        /// 使用首选扇区大小从文件中解析分区表。
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <param name="sectorSize">The sector size to prefer, or <see langword="null" /> for auto-detection.</param>
        /// <returns>The parsed partition table.</returns>
        public static IPartitionTable FromFile(string path, bool mutable, int? sectorSize)
        {
            return PartitionTableParser.FromFile(path, mutable, sectorSize);
        }
    }
}
