using FirmwareKit.PartitionTable.Exceptions;
using FirmwareKit.PartitionTable.Json.Services;
using FirmwareKit.PartitionTable.Models;
using FirmwareKit.PartitionTable.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace FirmwareKit.PartitionTable.Tests
{
    public class MbrUpdatePartitionTests
    {
        [Fact]
        public void UpdatePartition_ReplacesEntryCorrectly()
        {
            var mbr = CreateEmptyMbrImage();
            using var ms = new MemoryStream(mbr);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(ms, mutable: true));

            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });
            table.UpdatePartition(0, new MbrPartitionEntry { PartitionType = 0x0B, FirstLba = 4096, SectorCount = 2048 });

            Assert.Equal(0x0B, table.Partitions[0].PartitionType);
            Assert.Equal((uint)4096, table.Partitions[0].FirstLba);
            Assert.Equal((uint)2048, table.Partitions[0].SectorCount);
        }

        [Fact]
        public void UpdatePartition_ThrowsOnReadOnly()
        {
            var mbr = CreateEmptyMbrImage();
            using var ms = new MemoryStream(mbr);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(ms, mutable: false));

            Assert.Throws<PartitionOperationException>(() =>
                table.UpdatePartition(0, new MbrPartitionEntry { PartitionType = 0x07 }));
        }

        [Fact]
        public void UpdatePartition_ThrowsOnInvalidIndex()
        {
            var mbr = CreateEmptyMbrImage();
            using var ms = new MemoryStream(mbr);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(ms, mutable: true));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                table.UpdatePartition(-1, new MbrPartitionEntry()));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                table.UpdatePartition(4, new MbrPartitionEntry()));
        }

        private static byte[] CreateEmptyMbrImage()
        {
            var buffer = new byte[512];
            buffer[510] = 0x55;
            buffer[511] = 0xAA;
            return buffer;
        }
    }

    public class MbrQueryTests
    {
        [Fact]
        public void FindMbrPartitionsByType_ReturnsMatchingPartitions()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: true) as MbrPartitionTable;
            Assert.NotNull(table);
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });
            table.SetPartition(1, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 4096, SectorCount = 2048 });
            table.SetPartition(2, new MbrPartitionEntry { PartitionType = 0x0B, FirstLba = 8192, SectorCount = 1024 });

            var results = PartitionQuery.FindMbrPartitionsByType(table, 0x07);
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(0x07, r.Entry.PartitionType));
        }

        [Fact]
        public void FindMbrPartitionsByType_ReturnsEmptyWhenNoneMatch()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: true) as MbrPartitionTable;
            Assert.NotNull(table);
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });

            var results = PartitionQuery.FindMbrPartitionsByType(table, 0x83);
            Assert.Empty(results);
        }

        [Fact]
        public void FindFirstMbrPartitionByType_ReturnsFirstMatch()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: true) as MbrPartitionTable;
            Assert.NotNull(table);
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });
            table.SetPartition(1, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 4096, SectorCount = 2048 });

            var result = PartitionQuery.FindFirstMbrPartitionByType(table, 0x07);
            Assert.NotNull(result);
            Assert.Equal(0, result.Value.Index);
            Assert.Equal((uint)2048, result.Value.Entry.FirstLba);
        }

        [Fact]
        public void FindFirstMbrPartitionByType_ReturnsNullWhenNotFound()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: true) as MbrPartitionTable;
            Assert.NotNull(table);

            var result = PartitionQuery.FindFirstMbrPartitionByType(table, 0x83);
            Assert.Null(result);
        }

        [Fact]
        public void GetMbrFreeRanges_ReturnsCorrectRanges()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: true) as MbrPartitionTable;
            Assert.NotNull(table);
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 100, SectorCount = 100 });
            table.SetPartition(1, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 300, SectorCount = 100 });

            var freeRanges = PartitionQuery.GetMbrFreeRanges(table, totalSectors: 1000);
            Assert.NotEmpty(freeRanges);
            Assert.All(freeRanges, r => Assert.True(r.SectorCount > 0));
        }

        [Fact]
        public void GetMbrFreeRanges_NoPartitions_ReturnsFullDisk()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: false) as MbrPartitionTable;
            Assert.NotNull(table);

            var freeRanges = PartitionQuery.GetMbrFreeRanges(table, totalSectors: 1000);
            Assert.Single(freeRanges);
            Assert.Equal((uint)1, freeRanges[0].FirstLba);
            Assert.Equal((uint)999, freeRanges[0].SectorCount);
        }
    }

    public class AsyncWriteTests
    {
        [Fact]
        public async Task WriteToStreamAsync_WritesCorrectly()
        {
            var mbr = CreateEmptyMbrImage();
            using var source = new MemoryStream(mbr);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });

            using var target = new MemoryStream(new byte[512]);
            await PartitionTableWriter.WriteToStreamAsync(table, target);

            target.Position = 0;
            var reopened = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(target, mutable: false));
            Assert.Equal(0x07, reopened.Partitions[0].PartitionType);
        }

        [Fact]
        public async Task WriteToFileAtomicAsync_WritesFile()
        {
            var mbr = CreateEmptyMbrImage();
            using var source = new MemoryStream(mbr);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });

            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".img");
            try
            {
                await PartitionTableWriter.WriteToFileAtomicAsync(
                    table, tempPath,
                    requireConfirmation: true,
                    confirmation: "I_UNDERSTAND_PARTITION_WRITE");

                Assert.True(File.Exists(tempPath));
                var reopened = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromFile(tempPath));
                Assert.Equal(0x07, reopened.Partitions[0].PartitionType);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task WriteToFileAtomicAsync_RequiresConfirmation()
        {
            var mbr = CreateEmptyMbrImage();
            using var source = new MemoryStream(mbr);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));

            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".img");
            try
            {
                await Assert.ThrowsAsync<PartitionOperationException>(() =>
                    PartitionTableWriter.WriteToFileAtomicAsync(table, tempPath));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task WriteToStreamAsync_ThrowsOnNullTable()
        {
            using var stream = new MemoryStream();
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PartitionTableWriter.WriteToStreamAsync(null!, stream));
        }

        private static byte[] CreateEmptyMbrImage()
        {
            var buffer = new byte[512];
            buffer[510] = 0x55;
            buffer[511] = 0xAA;
            return buffer;
        }
    }

    public class PartitionTableValidatorTests
    {
        [Fact]
        public void Validate_ReadOnlyTable_ReturnsError()
        {
            var mbr = CreateEmptyMbrImage();
            using var ms = new MemoryStream(mbr);
            var table = PartitionTableReader.FromStream(ms, mutable: false);

            var result = PartitionTableValidator.Validate(table);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "TABLE_READ_ONLY");
        }

        [Fact]
        public void Validate_ValidGptTable_Passes()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry
            {
                Name = "Test",
                PartitionType = Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
                FirstLba = 2048,
                LastLba = 4095
            });

            var result = PartitionTableValidator.Validate(table);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_GptWithOverlappingPartitions_ReturnsError()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry
            {
                Name = "A",
                PartitionType = Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
                FirstLba = 2048,
                LastLba = 4095
            });
            table.AddPartition(new GptPartitionEntry
            {
                Name = "B",
                PartitionType = Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
                FirstLba = 3000,
                LastLba = 5000
            });

            var result = PartitionTableValidator.Validate(table);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "GPT_OVERLAP");
        }

        [Fact]
        public void Validate_GptWithReversedLba_ReturnsError()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry
            {
                Name = "Bad",
                PartitionType = Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
                FirstLba = 5000,
                LastLba = 2048
            });

            var result = PartitionTableValidator.Validate(table);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "GPT_LBA_ORDER");
        }

        [Fact]
        public void Validate_MbrWithInvalidStatus_ReturnsError()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: true) as MbrPartitionTable;
            Assert.NotNull(table);
            table.SetPartition(0, new MbrPartitionEntry { Status = 0x7F, PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });

            var result = PartitionTableValidator.Validate(table);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "MBR_STATUS_INVALID");
        }

        [Fact]
        public void Validate_AmlogicWithEmptyName_ReturnsError()
        {
            var table = PartitionTableBuilder.CreateAmlogicEpt().Build(mutable: true) as AmlogicPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new AmlogicPartitionEntry { Name = "", Offset = 0, Size = 4096 });

            var result = PartitionTableValidator.Validate(table);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "AMLOGIC_EPT_NAME_EMPTY");
        }

        [Fact]
        public void Validate_AmlogicWithDuplicateNames_ReturnsError()
        {
            var table = PartitionTableBuilder.CreateAmlogicEpt().Build(mutable: true) as AmlogicPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new AmlogicPartitionEntry { Name = "boot", Offset = 0, Size = 4096 });
            table.AddPartition(new AmlogicPartitionEntry { Name = "boot", Offset = 4096, Size = 4096 });

            var result = PartitionTableValidator.Validate(table);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "AMLOGIC_EPT_NAME_DUP");
        }

        [Fact]
        public void ValidateAndThrow_ThrowsOnInvalidTable()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: false);
            Assert.Throws<PartitionOperationException>(() => PartitionTableValidator.ValidateAndThrow(table));
        }

        [Fact]
        public void ValidateAndThrow_DoesNotThrowOnValidTable()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true);
            PartitionTableValidator.ValidateAndThrow(table);
        }

        private static byte[] CreateEmptyMbrImage()
        {
            var buffer = new byte[512];
            buffer[510] = 0x55;
            buffer[511] = 0xAA;
            return buffer;
        }
    }

    public class ExceptionSerializationTests
    {
        [Fact]
        public void PartitionTableException_PropertiesArePreserved()
        {
            var ex = new PartitionTableException("test message", "TEST_CODE", PartitionTableType.Gpt);
            Assert.Equal("test message", ex.Message);
            Assert.Equal("TEST_CODE", ex.ErrorCode);
            Assert.Equal(PartitionTableType.Gpt, ex.TableType);
        }

        [Fact]
        public void PartitionTableException_WithInnerException()
        {
            var inner = new IOException("io error");
            var ex = new PartitionTableException("test message", "TEST_CODE", PartitionTableType.Mbr, inner);
            Assert.Same(inner, ex.InnerException);
            Assert.Equal("TEST_CODE", ex.ErrorCode);
            Assert.Equal(PartitionTableType.Mbr, ex.TableType);
        }

        [Fact]
        public void PartitionOperationException_PropertiesArePreserved()
        {
            var ex = new PartitionOperationException("op failed", "OP_FAIL", 2, PartitionTableType.Mbr);
            Assert.Equal("OP_FAIL", ex.ErrorCode);
            Assert.Equal(2, ex.PartitionIndex);
            Assert.Equal(PartitionTableType.Mbr, ex.TableType);
        }

        [Fact]
        public void PartitionTableChecksumException_PropertiesArePreserved()
        {
            var ex = new PartitionTableChecksumException("crc failed", "CRC_FAIL", "HeaderCrc", PartitionTableType.Gpt);
            Assert.Equal("HeaderCrc", ex.ChecksumKind);
            Assert.Equal("CRC_FAIL", ex.ErrorCode);
            Assert.Equal(PartitionTableType.Gpt, ex.TableType);
        }

        [Fact]
        public void PartitionTableRepairException_PropertiesArePreserved()
        {
            var ex = new PartitionTableRepairException("repair failed", "REPAIR_FAIL", PartitionTableType.AmlogicEpt);
            Assert.Equal("REPAIR_FAIL", ex.ErrorCode);
            Assert.Equal(PartitionTableType.AmlogicEpt, ex.TableType);
        }

        [Fact]
        public void PartitionTableFormatException_PropertiesArePreserved()
        {
            var ex = new PartitionTableFormatException("format error", "FMT_ERR", PartitionTableType.Gpt);
            Assert.Equal("FMT_ERR", ex.ErrorCode);
            Assert.Equal(PartitionTableType.Gpt, ex.TableType);
        }
    }

    public class ManifestSchemaVersionTests
    {
        [Fact]
        public void ExportToJson_IncludesSchemaVersion()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: false);
            string json = PartitionTableManifestSerializer.ExportToJson(table, indented: false);

            Assert.Contains("\"SchemaVersion\":2", json);
        }

        [Fact]
        public void ImportFromJson_AcceptsCurrentVersion()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: false);
            string json = PartitionTableManifestSerializer.ExportToJson(table, indented: false);

            var manifest = PartitionTableManifestSerializer.ImportFromJson(json);
            Assert.Equal(PartitionTableManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        }

        [Fact]
        public void ImportFromJson_RejectsFutureVersion()
        {
            string json = "{\"SchemaVersion\":999,\"Kind\":\"Gpt\",\"GptPartitions\":[]}";

            Assert.Throws<InvalidOperationException>(() =>
                PartitionTableManifestSerializer.ImportFromJson(json));
        }

        [Fact]
        public void ImportFromJson_AcceptsVersion1()
        {
            string json = "{\"SchemaVersion\":1,\"Kind\":\"Gpt\",\"GptPartitions\":[]}";

            var manifest = PartitionTableManifestSerializer.ImportFromJson(json);
            Assert.Equal(1, manifest.SchemaVersion);
        }
    }

    public class ProviderInterfaceTests
    {
        [Fact]
        public void IPartitionTableWriter_IsDefined()
        {
            Assert.True(typeof(FirmwareKit.PartitionTable.Interfaces.IPartitionTableWriter).IsInterface);
        }

        [Fact]
        public void IPartitionTableDiagnosticsProvider_IsDefined()
        {
            Assert.True(typeof(FirmwareKit.PartitionTable.Interfaces.IPartitionTableDiagnosticsProvider).IsInterface);
        }

        [Fact]
        public void IPartitionTableRepairProvider_IsDefined()
        {
            Assert.True(typeof(FirmwareKit.PartitionTable.Interfaces.IPartitionTableRepairProvider).IsInterface);
        }
    }
}
