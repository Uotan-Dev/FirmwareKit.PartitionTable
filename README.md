# FirmwareKit.PartitionTable

A .NET partition table library for reading, parsing, editing, serializing, deserializing, and saving MBR, GPT, and Amlogic EPT partition tables.

## Features

- Detects Amlogic EPT, MBR, and GPT partition tables from a seekable stream or file.
- Auto-detects common GPT sector sizes (512/1024/2048/4096/8192).
- Supports editable and read-only table instances.
- Preserves stream position after parsing.
- Provides diagnostics for CRC/checksum, bounds, overlap, and hybrid MBR warnings.
- Supports conservative GPT CRC refresh/repair workflows.
- Supports high-level operation helpers (alignment and dry-run write planning).
- Supports table diffing and structured comparison reports.
- Supports advanced read options (strict sector-size and custom probe sizes).
- Supports async read APIs for service and UI scenarios.
- Supports JSON manifest import/export and manifest-to-table reconstruction via the optional `FirmwareKit.PartitionTable.Json` package.
- Supports atomic file writes with confirmation token.
- Uses `Crc32.NET` for CRC-32 calculation.
- Targets `netstandard2.0`, `net8.0`, and `net10.0`.

## Projects

- `FirmwareKit.PartitionTable` - core library.
- `FirmwareKit.PartitionTable.Json` - optional JSON manifest extension package.
- `FirmwareKit.PartitionTable.Cli` - sample command-line tool.
- `FirmwareKit.PartitionTable.Tests` - xUnit test project.

## Usage

```csharp
using System.IO;
using FirmwareKit.PartitionTable;

using var stream = File.OpenRead("disk.img");
IPartitionTable table = PartitionTableReader.FromStream(stream, mutable: true);

// Optional: specify sector size explicitly when working with uncommon images.
IPartitionTable tableWithSector = PartitionTableReader.FromStream(stream, mutable: false, sectorSize: 8192);

// Optional: strict probing and custom sector list.
var options = new PartitionReadOptions
{
 PreferredSectorSize = 4096,
 StrictSectorSize = true,
 ProbeSectorSizes = new[] { 4096, 8192 }
};
IPartitionTable strictTable = PartitionTableReader.FromStream(stream, mutable: false, options: options);
```

Diagnostics and repair:

```csharp
var report = PartitionTableDiagnostics.Analyze(table);
if (!report.IsHealthy)
{
 using var rw = File.Open("disk.img", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
 PartitionRepairResult repair = PartitionTableRepair.RepairGptCrcInPlace(rw, sectorSize: 4096);
}
```

Validate, repair, and diff tables:

```csharp
PartitionDiagnosticsReport diagnostics = PartitionTableDiagnostics.Analyze(table);
PartitionRepairResult repaired = PartitionTableRepair.RepairAnyInPlace(File.Open("disk.img", FileMode.Open, FileAccess.ReadWrite, FileShare.None));
PartitionTableDiff diff = PartitionTableOperations.Compare(leftTable, rightTable);
```

Manifest interoperability:

```csharp
// Requires the FirmwareKit.PartitionTable.Json package.
string json = PartitionTableManifestSerializer.ExportToJson(table);
PartitionTableManifest manifest = PartitionTableManifestSerializer.ImportFromJson(json);
IPartitionTable rebuilt = PartitionTableManifestSerializer.ToPartitionTable(manifest);
```

Handle Amlogic EPT tables:

```csharp
using var reserved = File.OpenRead("reserved-partition.img");
IPartitionTable table = PartitionTableReader.FromStream(reserved, mutable: false);
if (table is AmlogicPartitionTable ept)
{
 Console.WriteLine($"EPT checksum valid: {ept.IsChecksumValid}");
 foreach (var part in ept.Partitions)
 {
  Console.WriteLine($"{part.Name}: offset=0x{part.Offset:X}, size=0x{part.Size:X}, mask={part.MaskFlags}");
 }
}
```

Safety write:

```csharp
PartitionTableWriter.WriteToFileAtomic(
 table,
 "disk-fixed.img",
 requireConfirmation: true,
 confirmation: "I_UNDERSTAND_PARTITION_WRITE");
```

CLI usage:

```bash
dotnet run --project FirmwareKit.PartitionTable.Cli -- read disk.img --sector-size 8192
dotnet run --project FirmwareKit.PartitionTable.Cli -- read disk.img --json
dotnet run --project FirmwareKit.PartitionTable.Cli -- write in.img out.img --sector-size 4096 --dry-run
dotnet run --project FirmwareKit.PartitionTable.Cli -- validate disk.img --sector-size 4096
dotnet run --project FirmwareKit.PartitionTable.Cli -- repair disk.img --sector-size 4096
dotnet run --project FirmwareKit.PartitionTable.Cli -- diff left.img right.img --json
dotnet run --project FirmwareKit.PartitionTable.Cli -- export disk.img manifest.json --sector-size 4096
dotnet run --project FirmwareKit.PartitionTable.Cli -- import manifest.json disk.img --keep-backup
```

## Testing

The test project includes generated GPT fixtures and script-driven sample generation.

Run the test suite:

```bash
dotnet test FirmwareKit.PartitionTable.Tests/FirmwareKit.PartitionTable.Tests.csproj
```

## Test data generation

Use the PowerShell script under `scripts/Generate-TestData.ps1` to regenerate the GPT fixture used by the tests.
