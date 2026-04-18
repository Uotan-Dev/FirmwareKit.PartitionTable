# FirmwareKit.PartitionTable

A .NET partition table library for reading, parsing, editing, serializing, deserializing, and saving MBR and GPT partition tables.

## Features

- Detects MBR and GPT partition tables from a seekable stream or file.
- Auto-detects common GPT sector sizes.
- Supports editable and read-only table instances.
- Preserves stream position after parsing.
- Uses `Crc32.NET` for CRC-32 calculation.
- Targets `netstandard2.0`, `net6.0`, `net8.0`, and `net10.0`.

## Projects

- `FirmwareKit.PartitionTable` - core library.
- `FirmwareKit.PartitionTable.Cli` - sample command-line tool.
- `FirmwareKit.PartitionTable.Tests` - xUnit test project.

## Usage

```csharp
using System.IO;
using FirmwareKit.PartitionTable;

using var stream = File.OpenRead("disk.img");
IPartitionTable table = PartitionTableReader.FromStream(stream, mutable: true);
```

## Testing

The test project includes generated GPT fixtures and script-driven sample generation.

Run the test suite:

```bash
dotnet test FirmwareKit.PartitionTable.Tests/FirmwareKit.PartitionTable.Tests.csproj
```

## Test data generation

Use the PowerShell script under `scripts/Generate-TestData.ps1` to regenerate the GPT fixture used by the tests.
