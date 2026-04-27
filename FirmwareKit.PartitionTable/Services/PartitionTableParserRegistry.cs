using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Interface for pluggable partition table format parsers.
    /// 可插拔的分区表格式解析器接口。
    /// </summary>
    public interface IPartitionTableParser
    {
        /// <summary>
        /// Gets the partition table type this parser supports.
        /// 获取此解析器支持的分区表类型。
        /// </summary>
        PartitionTableType SupportedType { get; }

        /// <summary>
        /// Gets the priority of this parser (lower values are tried first).
        /// 获取此解析器的优先级（值越小越先尝试）。
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Attempts to parse a partition table from the given stream.
        /// 尝试从给定流中解析分区表。
        /// </summary>
        /// <param name="stream">The source stream positioned at the beginning of the disk image.</param>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <param name="sectorSize">The sector size in bytes.</param>
        /// <returns>The parsed partition table, or <see langword="null" /> if the format is not recognized.</returns>
        IPartitionTable? TryParse(Stream stream, bool mutable, int sectorSize);
    }

    /// <summary>
    /// A registry of pluggable partition table parsers, allowing custom formats to be registered.
    /// 可插拔分区表解析器注册表，允许注册自定义格式。
    /// </summary>
    public sealed class PartitionTableParserRegistry
    {
        private static readonly object s_lock = new object();
        private static PartitionTableParserRegistry? s_default;

        private readonly List<IPartitionTableParser> _parsers = new List<IPartitionTableParser>();

        /// <summary>
        /// Gets the default registry pre-configured with Amlogic EPT, GPT, and MBR parsers.
        /// 获取预配置了 Amlogic EPT、GPT 和 MBR 解析器的默认注册表。
        /// </summary>
        public static PartitionTableParserRegistry Default
        {
            get
            {
                if (s_default == null)
                {
                    lock (s_lock)
                    {
                        s_default ??= CreateDefault();
                    }
                }

                return s_default;
            }
        }

        /// <summary>
        /// Gets the registered parsers in priority order.
        /// 获取按优先级排序的已注册解析器。
        /// </summary>
        public IReadOnlyList<IPartitionTableParser> Parsers => _parsers.AsReadOnly();

        /// <summary>
        /// Registers a parser. Parsers are sorted by priority after registration.
        /// 注册解析器。注册后按优先级排序。
        /// </summary>
        /// <param name="parser">The parser to register.</param>
        public void Register(IPartitionTableParser parser)
        {
            if (parser == null) throw new ArgumentNullException(nameof(parser));
            _parsers.Add(parser);
            _parsers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// Unregisters all parsers for the specified partition table type.
        /// 注销指定分区表类型的所有解析器。
        /// </summary>
        /// <param name="type">The partition table type to unregister.</param>
        /// <returns><see langword="true" /> if any parsers were removed.</returns>
        public bool Unregister(PartitionTableType type)
        {
            return _parsers.RemoveAll(p => p.SupportedType == type) > 0;
        }

        /// <summary>
        /// Removes all registered parsers.
        /// 移除所有已注册的解析器。
        /// </summary>
        public void Clear()
        {
            _parsers.Clear();
        }

        /// <summary>
        /// Attempts to parse a partition table using the registered parsers in priority order.
        /// 尝试按优先级顺序使用已注册的解析器解析分区表。
        /// </summary>
        /// <param name="stream">The source stream.</param>
        /// <param name="mutable">Whether the returned table should be editable.</param>
        /// <param name="sectorSize">The sector size in bytes.</param>
        /// <returns>The parsed partition table, or <see langword="null" /> if no parser recognizes the format.</returns>
        public IPartitionTable? TryParse(Stream stream, bool mutable, int sectorSize)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            long originalPosition = stream.Position;
            try
            {
                for (int i = 0; i < _parsers.Count; i++)
                {
                    stream.Position = originalPosition;
                    var result = _parsers[i].TryParse(stream, mutable, sectorSize);
                    if (result != null)
                    {
                        return result;
                    }
                }

                return null;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        private static PartitionTableParserRegistry CreateDefault()
        {
            var registry = new PartitionTableParserRegistry();
            registry.Register(new AmlogicEptParserAdapter());
            registry.Register(new GptParserAdapter());
            registry.Register(new MbrParserAdapter());
            return registry;
        }

        private sealed class AmlogicEptParserAdapter : IPartitionTableParser
        {
            public PartitionTableType SupportedType => PartitionTableType.AmlogicEpt;
            public int Priority => 0;

            public IPartitionTable? TryParse(Stream stream, bool mutable, int sectorSize)
            {
                return PartitionTableParser.TryReadAmlogicEpt(stream, mutable);
            }
        }

        private sealed class GptParserAdapter : IPartitionTableParser
        {
            public PartitionTableType SupportedType => PartitionTableType.Gpt;
            public int Priority => 1;

            public IPartitionTable? TryParse(Stream stream, bool mutable, int sectorSize)
            {
                return PartitionTableParser.TryReadGptWithSectorSize(stream, mutable, sectorSize);
            }
        }

        private sealed class MbrParserAdapter : IPartitionTableParser
        {
            public PartitionTableType SupportedType => PartitionTableType.Mbr;
            public int Priority => 2;

            public IPartitionTable? TryParse(Stream stream, bool mutable, int sectorSize)
            {
                return PartitionTableParser.TryReadMbrTable(stream, mutable);
            }
        }
    }
}
