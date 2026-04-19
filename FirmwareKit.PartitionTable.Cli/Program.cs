using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using FirmwareKit.PartitionTable.Services;
using System;
using System.IO;
using System.Text.Json;

namespace FirmwareKit.PartitionTable.Cli
{
    /// <summary>
    /// CLI entry point for partition table read/write operations.
    /// 分区表读写操作的命令行入口。
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Executes the CLI command.
        /// 执行命令行指令。
        /// </summary>
        /// <param name="args">Command-line arguments. / 命令行参数。</param>
        /// <returns>Process exit code. / 进程退出码。</returns>
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();
                switch (command)
                {
                    case "read":
                        return ExecuteRead(args);
                    case "write":
                        return ExecuteWrite(args);
                    default:
                        Console.Error.WriteLine($"Unknown command: {command}");
                        PrintUsage();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"错误：{ex.Message}");
                return 100;
            }
        }

        private static int ExecuteRead(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 2;
            }

            var path = args[1];
            if (!TryParseOptions(args, 2, allowJson: true, out bool mutable, out int? sectorSize, out bool json, out string? optionError))
            {
                Console.Error.WriteLine(optionError);
                PrintUsage();
                return 2;
            }

            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File not found: {path}");
                return 3;
            }

            using var stream = File.OpenRead(path);
            var table = PartitionTableReader.FromStream(stream, mutable, sectorSize);
            if (json)
            {
                WriteAsJson(table);
                return 0;
            }

            Console.WriteLine($"类型: {table.Type}");
            Console.WriteLine($"是否可变: {table.IsMutable}");

            if (table.Type == PartitionTableType.Mbr && table is MbrPartitionTable mbr)
            {
                for (int i = 0; i < mbr.Partitions.Length; i++)
                {
                    var entry = mbr.Partitions[i];
                    Console.WriteLine($"MBR partition[{i}] Type: 0x{entry.PartitionType:X2} FirstLBA: {entry.FirstLba} Length: {entry.SectorCount}");
                }
            }
            else if (table.Type == PartitionTableType.Gpt && table is GptPartitionTable gpt)
            {
                Console.WriteLine($"DiskGuid: {gpt.DiskGuid}");
                Console.WriteLine($"Usable sectors: {gpt.FirstUsableLba}-{gpt.LastUsableLba}");
                for (int i = 0; i < gpt.Partitions.Count; i++)
                {
                    var entry = gpt.Partitions[i];
                    Console.WriteLine($"GPT partition[{i}] Type: {entry.PartitionType} Id: {entry.PartitionId} {entry.FirstLba}-{entry.LastLba} Name: {entry.Name}");
                }
            }
            else if (table.Type == PartitionTableType.AmlogicEpt && table is AmlogicPartitionTable amlogic)
            {
                Console.WriteLine($"Checksum valid: {amlogic.IsChecksumValid}");
                for (int i = 0; i < amlogic.Partitions.Count; i++)
                {
                    var entry = amlogic.Partitions[i];
                    Console.WriteLine($"EPT partition[{i}] Name: {entry.Name} Offset: 0x{entry.Offset:X} Size: 0x{entry.Size:X} Mask: {entry.MaskFlags}");
                }
            }
            else
            {
                Console.WriteLine("Unknown partition table type");
            }
            return 0;
        }

        private static int ExecuteWrite(string[] args)
        {
            if (args.Length < 3)
            {
                PrintUsage();
                return 2;
            }

            var src = args[1];
            var dest = args[2];
            if (!TryParseOptions(args, 3, allowJson: false, out bool mutable, out int? sectorSize, out _, out string? optionError))
            {
                Console.Error.WriteLine(optionError);
                PrintUsage();
                return 2;
            }

            if (!File.Exists(src))
            {
                Console.Error.WriteLine($"Source file not found: {src}");
                return 3;
            }

            using var stream = File.OpenRead(src);
            var table = PartitionTableReader.FromStream(stream, mutable, sectorSize);
            if (!table.IsMutable)
            {
                Console.Error.WriteLine("The partition table is read-only and cannot be written.");
                return 4;
            }

            using var outStream = File.Open(dest, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            table.WriteToStream(outStream);
            Console.WriteLine($"Write completed: {dest}");
            return 0;
        }

        private static bool TryParseOptions(string[] args, int startIndex, bool allowJson, out bool mutable, out int? sectorSize, out bool json, out string? error)
        {
            mutable = false;
            sectorSize = null;
            json = false;
            error = null;

            for (int i = startIndex; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--mutable", StringComparison.OrdinalIgnoreCase))
                {
                    mutable = true;
                    continue;
                }

                if (arg.Equals("--sector-size", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        error = "Missing value for --sector-size.";
                        return false;
                    }

                    if (!int.TryParse(args[i + 1], out int parsed) || parsed <= 0)
                    {
                        error = "--sector-size must be a positive integer.";
                        return false;
                    }

                    sectorSize = parsed;
                    i++;
                    continue;
                }

                if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allowJson)
                    {
                        error = "--json is only supported by the read command.";
                        return false;
                    }

                    json = true;
                    continue;
                }

                error = $"Unknown option: {arg}";
                return false;
            }

            return true;
        }

        private static void WriteAsJson(IPartitionTable table)
        {
            object payload;
            if (table is MbrPartitionTable mbr)
            {
                payload = new
                {
                    type = table.Type.ToString(),
                    isMutable = table.IsMutable,
                    signature = mbr.Signature,
                    isProtectiveGpt = mbr.IsProtectiveGpt,
                    partitions = mbr.Partitions
                };
            }
            else if (table is GptPartitionTable gpt)
            {
                payload = new
                {
                    type = table.Type.ToString(),
                    isMutable = table.IsMutable,
                    sectorSize = gpt.SectorSize,
                    diskGuid = gpt.DiskGuid,
                    firstUsableLba = gpt.FirstUsableLba,
                    lastUsableLba = gpt.LastUsableLba,
                    isHeaderCrcValid = gpt.IsHeaderCrcValid,
                    isEntryTableCrcValid = gpt.IsEntryTableCrcValid,
                    partitions = gpt.Partitions
                };
            }
            else if (table is AmlogicPartitionTable amlogic)
            {
                payload = new
                {
                    type = table.Type.ToString(),
                    isMutable = table.IsMutable,
                    isChecksumValid = amlogic.IsChecksumValid,
                    versionWords = amlogic.VersionWords,
                    recordedChecksum = amlogic.RecordedChecksum,
                    partitions = amlogic.Partitions
                };
            }
            else
            {
                payload = new
                {
                    type = table.Type.ToString(),
                    isMutable = table.IsMutable
                };
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            Console.WriteLine(JsonSerializer.Serialize(payload, options));
        }

        private static void PrintUsage()
        {
            Console.WriteLine("FirmwareKit.PartitionTable.Cli");
            Console.WriteLine("Usage:");
            Console.WriteLine("  read <path> [--mutable] [--sector-size <bytes>] [--json]");
            Console.WriteLine("  write <src> <dest> [--mutable] [--sector-size <bytes>]");
        }
    }
}
