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
                    case "validate":
                        return ExecuteValidate(args);
                    case "repair":
                        return ExecuteRepair(args);
                    case "diff":
                        return ExecuteDiff(args);
                    case "export":
                        return ExecuteExport(args);
                    case "import":
                        return ExecuteImport(args);
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
            if (!TryParseOptions(args, 2, allowJson: true, allowDryRun: false, allowKeepBackup: false, out bool mutable, out int? sectorSize, out bool json, out bool dryRun, out bool keepBackup, out string? optionError))
            {
                Console.Error.WriteLine(optionError);
                PrintUsage();
                return 2;
            }

            if (dryRun || keepBackup)
            {
                Console.Error.WriteLine("--dry-run and --keep-backup are not supported by read.");
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
            if (!TryParseOptions(args, 3, allowJson: false, allowDryRun: true, allowKeepBackup: true, out bool mutable, out int? sectorSize, out _, out bool dryRun, out bool keepBackup, out string? optionError))
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
            var table = PartitionTableReader.FromStream(stream, mutable: true, sectorSize);
            if (dryRun)
            {
                PrintWritePlan(table);
                return 0;
            }

            PartitionTableWriter.WriteToFileAtomic(
                table,
                dest,
                requireConfirmation: true,
                confirmation: "I_UNDERSTAND_PARTITION_WRITE",
                keepBackup: keepBackup);
            Console.WriteLine($"Write completed: {dest}");
            return 0;
        }

        private static int ExecuteValidate(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 2;
            }

            var path = args[1];
            if (!TryParseOptions(args, 2, allowJson: false, allowDryRun: false, allowKeepBackup: false, out _, out int? sectorSize, out _, out _, out _, out string? optionError))
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
            var table = PartitionTableReader.FromStream(stream, mutable: false, sectorSize);
            PartitionDiagnosticsReport report = PartitionTableDiagnostics.Analyze(table);

            Console.WriteLine($"Type: {table.Type}");
            Console.WriteLine($"Healthy: {report.IsHealthy}");
            foreach (var issue in report.Issues)
            {
                Console.WriteLine($"[{issue.Severity}] {issue.Code}: {issue.Message} (auto-repair: {issue.CanAutoRepair})");
            }

            return report.IsHealthy ? 0 : 10;
        }

        private static int ExecuteRepair(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 2;
            }

            var path = args[1];
            if (!TryParseOptions(args, 2, allowJson: false, allowDryRun: true, allowKeepBackup: false, out _, out int? sectorSize, out _, out bool dryRun, out _, out string? optionError))
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

            if (dryRun)
            {
                byte[] image = File.ReadAllBytes(path);
                using var preview = new MemoryStream(image, writable: true);
                PartitionRepairResult previewResult = PartitionTableRepair.RepairAnyInPlace(preview, sectorSize);
                foreach (var action in previewResult.Actions)
                {
                    Console.WriteLine(action);
                }

                return previewResult.Repaired ? 0 : 10;
            }

            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            PartitionRepairResult result = PartitionTableRepair.RepairAnyInPlace(stream, sectorSize);
            foreach (var action in result.Actions)
            {
                Console.WriteLine(action);
            }

            return result.Repaired ? 0 : 10;
        }

        private static int ExecuteDiff(string[] args)
        {
            if (args.Length < 3)
            {
                PrintUsage();
                return 2;
            }

            var leftPath = args[1];
            var rightPath = args[2];
            if (!TryParseOptions(args, 3, allowJson: true, allowDryRun: false, allowKeepBackup: false, out _, out int? sectorSize, out bool json, out _, out _, out string? optionError))
            {
                Console.Error.WriteLine(optionError);
                PrintUsage();
                return 2;
            }

            if (!File.Exists(leftPath))
            {
                Console.Error.WriteLine($"File not found: {leftPath}");
                return 3;
            }

            if (!File.Exists(rightPath))
            {
                Console.Error.WriteLine($"File not found: {rightPath}");
                return 3;
            }

            using var leftStream = File.OpenRead(leftPath);
            using var rightStream = File.OpenRead(rightPath);
            var left = PartitionTableReader.FromStream(leftStream, mutable: false, sectorSize);
            var right = PartitionTableReader.FromStream(rightStream, mutable: false, sectorSize);
            PartitionTableDiff diff = PartitionTableOperations.Compare(left, right);

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(diff, new JsonSerializerOptions { WriteIndented = true }));
                return diff.HasDifferences ? 10 : 0;
            }

            Console.WriteLine($"Left: {left.Type}");
            Console.WriteLine($"Right: {right.Type}");
            Console.WriteLine($"Differences: {diff.Entries.Count}");
            foreach (var entry in diff.Entries)
            {
                Console.WriteLine($"[{entry.Kind}] #{entry.Index}: {entry.Description}");
                if (!string.IsNullOrEmpty(entry.Left))
                {
                    Console.WriteLine($"  Left: {entry.Left}");
                }
                if (!string.IsNullOrEmpty(entry.Right))
                {
                    Console.WriteLine($"  Right: {entry.Right}");
                }
            }

            return diff.HasDifferences ? 10 : 0;
        }

        private static int ExecuteExport(string[] args)
        {
            if (args.Length < 3)
            {
                PrintUsage();
                return 2;
            }

            var imagePath = args[1];
            var manifestPath = args[2];
            if (!TryParseOptions(args, 3, allowJson: false, allowDryRun: false, allowKeepBackup: false, out _, out int? sectorSize, out _, out _, out _, out string? optionError))
            {
                Console.Error.WriteLine(optionError);
                PrintUsage();
                return 2;
            }

            if (!File.Exists(imagePath))
            {
                Console.Error.WriteLine($"File not found: {imagePath}");
                return 3;
            }

            using var stream = File.OpenRead(imagePath);
            var table = PartitionTableReader.FromStream(stream, mutable: false, sectorSize);
            PartitionTableManifestSerializer.ExportToFile(table, manifestPath, indented: true);
            Console.WriteLine($"Manifest exported: {manifestPath}");
            return 0;
        }

        private static int ExecuteImport(string[] args)
        {
            if (args.Length < 3)
            {
                PrintUsage();
                return 2;
            }

            var manifestPath = args[1];
            var imagePath = args[2];
            if (!TryParseOptions(args, 3, allowJson: false, allowDryRun: true, allowKeepBackup: true, out _, out _, out _, out bool dryRun, out bool keepBackup, out string? optionError))
            {
                Console.Error.WriteLine(optionError);
                PrintUsage();
                return 2;
            }

            if (!File.Exists(manifestPath))
            {
                Console.Error.WriteLine($"File not found: {manifestPath}");
                return 3;
            }

            PartitionTableManifest manifest = PartitionTableManifestSerializer.ImportFromFile(manifestPath);
            IPartitionTable table = PartitionTableManifestSerializer.ToPartitionTable(manifest, mutable: true);
            if (dryRun)
            {
                PrintWritePlan(table);
                return 0;
            }

            PartitionTableWriter.WriteToFileAtomic(
                table,
                imagePath,
                requireConfirmation: true,
                confirmation: "I_UNDERSTAND_PARTITION_WRITE",
                keepBackup: keepBackup);

            Console.WriteLine($"Image imported: {imagePath}");
            return 0;
        }

        private static bool TryParseOptions(string[] args, int startIndex, bool allowJson, bool allowDryRun, bool allowKeepBackup, out bool mutable, out int? sectorSize, out bool json, out bool dryRun, out bool keepBackup, out string? error)
        {
            mutable = false;
            sectorSize = null;
            json = false;
            dryRun = false;
            keepBackup = false;
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

                if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allowDryRun)
                    {
                        error = "--dry-run is not supported by this command.";
                        return false;
                    }

                    dryRun = true;
                    continue;
                }

                if (arg.Equals("--keep-backup", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allowKeepBackup)
                    {
                        error = "--keep-backup is not supported by this command.";
                        return false;
                    }

                    keepBackup = true;
                    continue;
                }

                error = $"Unknown option: {arg}";
                return false;
            }

            return true;
        }

        private static void PrintWritePlan(IPartitionTable table)
        {
            PartitionWritePlan plan = PartitionTableOperations.BuildWritePlan(table);
            Console.WriteLine($"Type: {plan.Type}");
            foreach (var range in plan.Ranges)
            {
                Console.WriteLine($"{range.Description}: offset={range.Offset}, length={range.Length}");
            }
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
            Console.WriteLine("  write <src> <dest> [--mutable] [--sector-size <bytes>] [--dry-run] [--keep-backup]");
            Console.WriteLine("  validate <path> [--sector-size <bytes>]");
            Console.WriteLine("  repair <path> [--sector-size <bytes>] [--dry-run]");
            Console.WriteLine("  diff <left> <right> [--sector-size <bytes>] [--json]");
            Console.WriteLine("  export <image> <manifest.json> [--sector-size <bytes>]");
            Console.WriteLine("  import <manifest.json> <image> [--keep-backup] [--dry-run]");
        }
    }
}
