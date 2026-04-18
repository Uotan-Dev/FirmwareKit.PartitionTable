using System;
using System.IO;
using FirmwareKit.PartitionTable;

namespace FirmwareKit.PartitionTable.Cli
{
    public static class Program
    {
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
            bool mutable = args.Length >= 3 && args[2].Equals("--mutable", StringComparison.OrdinalIgnoreCase);

            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File not found: {path}");
                return 3;
            }

            using var stream = File.OpenRead(path);
            var table = PartitionTableReader.FromStream(stream, mutable);
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
            bool mutable = args.Length >= 4 && args[3].Equals("--mutable", StringComparison.OrdinalIgnoreCase);

            if (!File.Exists(src))
            {
                Console.Error.WriteLine($"Source file not found: {src}");
                return 3;
            }

            using var stream = File.OpenRead(src);
            var table = PartitionTableReader.FromStream(stream, mutable);
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

        private static void PrintUsage()
        {
            Console.WriteLine("FirmwareKit.PartitionTable.Cli");
            Console.WriteLine("Usage:");
            Console.WriteLine("  read <path> [--mutable]");
            Console.WriteLine("  write <src> <dest> [--mutable]");
        }
    }
}
