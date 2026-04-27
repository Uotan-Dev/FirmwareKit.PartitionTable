# FirmwareKit.PartitionTable.Json

JSON manifest import/export extensions for [FirmwareKit.PartitionTable](https://www.nuget.org/packages/FirmwareKit.PartitionTable).

## Installation

```bash
dotnet add package FirmwareKit.PartitionTable.Json
```

## Overview

This package adds JSON manifest support to `FirmwareKit.PartitionTable`, enabling you to:

- Export any partition table (MBR, GPT, or Amlogic EPT) to a JSON manifest string or file.
- Import a partition table from a JSON manifest string or file.
- Reconstruct a fully functional `IPartitionTable` instance from a manifest.

## Usage

Add the namespace:

```csharp
using FirmwareKit.PartitionTable.Services;
```

### Export to JSON

```csharp
using FirmwareKit.PartitionTable;

using var stream = File.OpenRead("disk.img");
IPartitionTable table = PartitionTableReader.FromStream(stream, mutable: false);

string json = PartitionTableManifestSerializer.ExportToJson(table);
```

### Export to file

```csharp
PartitionTableManifestSerializer.ExportToFile(table, "manifest.json");
```

### Import from JSON

```csharp
PartitionTableManifest manifest = PartitionTableManifestSerializer.ImportFromJson(json);
```

### Import from file

```csharp
PartitionTableManifest manifest = PartitionTableManifestSerializer.ImportFromFile("manifest.json");
```

### Reconstruct a partition table

```csharp
IPartitionTable rebuilt = PartitionTableManifestSerializer.ToPartitionTable(manifest, mutable: true);
```

Or directly from a JSON string:

```csharp
IPartitionTable rebuilt = PartitionTableManifestSerializer.ToPartitionTable(json, mutable: true);
```

## Supported partition table types

| Kind | Manifest `Kind` value |
|---|---|
| Master Boot Record | `Mbr` |
| GUID Partition Table | `Gpt` |
| Amlogic EPT | `AmlogicEpt` |

## Dependencies

- `FirmwareKit.PartitionTable` — core partition table library.
- `System.Text.Json` — only on `netstandard2.0` and `netstandard2.1`; built-in on all .NET 6+ targets.

## Target frameworks

`net10.0`, `net9.0`, `net8.0`, `net6.0`, `netstandard2.1`, `netstandard2.0`
