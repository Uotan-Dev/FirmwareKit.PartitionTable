using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FirmwareKit.PartitionTable.Services
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
        /// Parses a partition table from a seekable stream using advanced probing options.
        /// 使用高级探测选项从可寻址流中解析分区表。
        /// </summary>
        /// <param name="stream">The source stream.</param>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <param name="options">Read options.</param>
        /// <returns>The parsed partition table.</returns>
        public static IPartitionTable FromStream(Stream stream, bool mutable, PartitionReadOptions? options)
        {
            return PartitionTableParser.FromStream(stream, mutable, options);
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

        /// <summary>
        /// Parses a partition table from a file using advanced probing options.
        /// 使用高级探测选项从文件中解析分区表。
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <param name="options">Read options.</param>
        /// <returns>The parsed partition table.</returns>
        public static IPartitionTable FromFile(string path, bool mutable, PartitionReadOptions? options)
        {
            return PartitionTableParser.FromFile(path, mutable, options);
        }

        /// <summary>
        /// Parses a partition table asynchronously.
        /// 异步解析分区表。
        /// </summary>
        public static Task<IPartitionTable> FromStreamAsync(Stream stream, bool mutable = false, PartitionReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return PartitionTableParser.FromStream(stream, mutable, options);
            }, cancellationToken);
        }

        /// <summary>
        /// Parses a partition table from a file asynchronously.
        /// 从文件异步解析分区表。
        /// </summary>
        public static Task<IPartitionTable> FromFileAsync(string path, bool mutable = false, PartitionReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return PartitionTableParser.FromFile(path, mutable, options);
            }, cancellationToken);
        }
    }
}
