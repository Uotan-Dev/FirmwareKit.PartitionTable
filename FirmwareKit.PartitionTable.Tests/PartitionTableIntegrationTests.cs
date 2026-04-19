using FirmwareKit.PartitionTable.Models;
using FirmwareKit.PartitionTable.Services;
using FirmwareKit.PartitionTable.Util;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace FirmwareKit.PartitionTable.Tests
{
    public class PartitionTableIntegrationTests
    {
        [Fact]
        public void Diagnostics_DetectsGptCrcMismatch()
        {
            byte[] image = CreateSampleGptImage(512);
            image[510] = 0;
            image[511] = 0;
            image[512 + 16] ^= 0xFF;

            using var stream = new MemoryStream(image);
            var gpt = Assert.IsType<GptPartitionTable>(PartitionTableReader.FromStream(stream, mutable: true));

            PartitionDiagnosticsReport report = PartitionTableDiagnostics.Analyze(gpt);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Issues, i => i.Code == "GPT_HEADER_CRC");
        }

        [Fact]
        public void Repair_RewritesGptAndRestoresHealth()
        {
            byte[] image = CreateSampleGptImage(512);
            image[510] = 0;
            image[511] = 0;
            image[512 + 16] ^= 0xFF;

            using var stream = new MemoryStream(image);
            PartitionRepairResult repair = PartitionTableRepair.RepairGptCrcInPlace(stream, 512);

            Assert.True(repair.Repaired);
            stream.Position = 0;

            var repaired = Assert.IsType<GptPartitionTable>(PartitionTableReader.FromStream(stream, mutable: false, sectorSize: 512));
            Assert.Equal(512, repaired.SectorSize);
        }

        [Fact]
        public void Operations_CanPlanAlignedPartition()
        {
            byte[] image = CreateSampleGptImage(512);
            using var stream = new MemoryStream(image);
            var table = Assert.IsType<GptPartitionTable>(PartitionTableReader.FromStream(stream, mutable: false, sectorSize: 512));

            var plan = PartitionTableOperations.PlanAlignedGptPartition(
                table,
                sectorCount: 16,
                alignmentLba: 8,
                name: "NEW",
                partitionType: Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
                partitionId: Guid.NewGuid());

            Assert.Equal((ulong)80, plan.FirstLba);
            Assert.Equal((ulong)95, plan.LastLba);
            Assert.Equal("NEW", plan.Name);
        }

        [Fact]
        public void Operations_PlanIgnoresPartitionsOutsideUsableRange()
        {
            byte[] image = CreateSampleGptImage(512);
            using var stream = new MemoryStream(image);
            var table = Assert.IsType<GptPartitionTable>(PartitionTableReader.FromStream(stream, mutable: true, sectorSize: 512));

            table.UpdatePartition(0, new GptPartitionEntry
            {
                PartitionType = Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
                PartitionId = Guid.NewGuid(),
                FirstLba = 0,
                LastLba = 10,
                Name = "OUT_OF_RANGE"
            });

            var plan = PartitionTableOperations.PlanAlignedGptPartition(
                table,
                sectorCount: 8,
                alignmentLba: 8,
                name: "NEW",
                partitionType: Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
                partitionId: Guid.NewGuid());

            Assert.True(plan.FirstLba >= table.FirstUsableLba);
        }

        [Fact]
        public void Operations_BuildWritePlan_ForGptContainsExpectedRanges()
        {
            byte[] image = CreateSampleGptImage(512);
            using var stream = new MemoryStream(image);
            var table = Assert.IsType<GptPartitionTable>(PartitionTableReader.FromStream(stream, mutable: false, sectorSize: 512));

            PartitionWritePlan writePlan = PartitionTableOperations.BuildWritePlan(table);
            Assert.Equal(PartitionTableType.Gpt, writePlan.Type);
            Assert.True(writePlan.Ranges.Count >= 5);
            Assert.Contains(writePlan.Ranges, r => r.Description.Contains("primary header", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(writePlan.Ranges, r => r.Description.Contains("backup header", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Reader_AsyncAndStrictOptions_WorkAsExpected()
        {
            byte[] image = CreateSampleGptImage(4096);
            using var stream = new MemoryStream(image);

            var options = new PartitionReadOptions
            {
                PreferredSectorSize = 512,
                StrictSectorSize = true
            };

            await Assert.ThrowsAsync<InvalidDataException>(() => PartitionTableReader.FromStreamAsync(stream, mutable: false, options: options));

            stream.Position = 0;
            var parsed = await PartitionTableReader.FromStreamAsync(stream, mutable: false, options: new PartitionReadOptions { PreferredSectorSize = 4096, StrictSectorSize = true });
            var gpt = Assert.IsType<GptPartitionTable>(parsed);
            Assert.Equal(4096, gpt.SectorSize);
        }

        [Fact]
        public void Manifest_ExportImport_RoundTripsKind()
        {
            byte[] image = CreateSampleGptImage(512);
            using var stream = new MemoryStream(image);
            var table = PartitionTableReader.FromStream(stream, mutable: false, sectorSize: 512);

            string json = PartitionTableManifestSerializer.ExportToJson(table, indented: false);
            PartitionTableManifest manifest = PartitionTableManifestSerializer.ImportFromJson(json);

            Assert.Equal("Gpt", manifest.Kind);
            Assert.NotNull(manifest.DiskGuid);
            Assert.NotEmpty(manifest.GptPartitions);
        }

        [Fact]
        public void Writer_AtomicWrite_RequiresConfirmation()
        {
            byte[] image = CreateEmptyMbrImage();
            using var source = new MemoryStream(image);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });

            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".img");
            try
            {
                Assert.Throws<InvalidOperationException>(() => PartitionTableWriter.WriteToFileAtomic(table, tempPath));

                PartitionTableWriter.WriteToFileAtomic(table, tempPath, requireConfirmation: true, confirmation: "I_UNDERSTAND_PARTITION_WRITE");
                var reopened = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromFile(tempPath));
                Assert.Equal((byte)0x07, reopened.Partitions[0].PartitionType);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        [Fact]
        public void DevicePath_Normalize_WorksForRawAndRelativePaths()
        {
            string raw = DevicePath.Normalize("\\\\.\\PhysicalDrive0");
            Assert.Equal("\\\\.\\PhysicalDrive0", raw);

            string relative = DevicePath.Normalize(".\\FirmwareKit.PartitionTable.Tests\\test.gpt");
            Assert.True(Path.IsPathRooted(relative));
        }

        [Fact]
        public void Fuzz_GptHeaderMutations_DoNotCrashParser()
        {
            byte[] baseline = CreateSampleGptImage(512);
            var random = new Random(1234);

            for (int i = 0; i < 64; i++)
            {
                byte[] image = (byte[])baseline.Clone();
                image[510] = 0;
                image[511] = 0;

                int offset = 512 + random.Next(8, 92);
                image[offset] ^= (byte)random.Next(1, 255);

                using var stream = new MemoryStream(image);
                try
                {
                    PartitionTableReader.FromStream(stream, mutable: false);
                }
                catch (InvalidDataException)
                {
                    // expected for malformed samples
                }
                catch (ArgumentOutOfRangeException)
                {
                    // still acceptable defensive rejection
                }
            }
        }

        private static byte[] CreateEmptyMbrImage()
        {
            var buffer = new byte[512];
            buffer[510] = 0x55;
            buffer[511] = 0xAA;
            return buffer;
        }

        private static byte[] CreateSampleGptImage(int sectorSize)
        {
            int totalSectors = 128;
            byte[] image = new byte[sectorSize * totalSectors];

            byte[] entryBuffer = new byte[4 * 128];
            WriteGptEntry(entryBuffer, 0, Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"), Guid.Parse("11111111-2222-3333-4444-555555555555"), 40, 79, 0, "DATA");

            uint entryCrc32 = Crc32.Compute(entryBuffer);

            WriteProtectiveMbr(image);
            WriteGptHeader(image, sectorSize, 1, 127, 2, 4, 128, entryCrc32, Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"));

            Buffer.BlockCopy(entryBuffer, 0, image, sectorSize * 2, entryBuffer.Length);
            Buffer.BlockCopy(entryBuffer, 0, image, sectorSize * 126, entryBuffer.Length);

            byte[] backupHeader = BuildGptHeaderBytes(sectorSize, 127, 1, 126, 4, 128, entryCrc32, Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"), 34, 125);
            Buffer.BlockCopy(backupHeader, 0, image, sectorSize * 127, backupHeader.Length);

            return image;
        }

        private static void WriteProtectiveMbr(byte[] image)
        {
            image[510] = 0x55;
            image[511] = 0xAA;
            image[446 + 4] = 0xEE;
            BitConverter.GetBytes((uint)1).CopyTo(image, 446 + 8);
            BitConverter.GetBytes(uint.MaxValue).CopyTo(image, 446 + 12);
        }

        private static void WriteGptHeader(byte[] image, int sectorSize, ulong currentLba, ulong backupLba, ulong entriesLba, uint count, uint entrySize, uint entryCrc32, Guid diskGuid)
        {
            byte[] header = BuildGptHeaderBytes(sectorSize, currentLba, backupLba, entriesLba, count, entrySize, entryCrc32, diskGuid, 34, 125);
            Buffer.BlockCopy(header, 0, image, sectorSize, header.Length);
        }

        private static byte[] BuildGptHeaderBytes(int sectorSize, ulong currentLba, ulong backupLba, ulong entriesLba, uint count, uint entrySize, uint entryCrc32, Guid diskGuid, ulong firstUsable, ulong lastUsable)
        {
            byte[] header = new byte[sectorSize];
            Encoding.ASCII.GetBytes("EFI PART").CopyTo(header, 0);
            WriteUInt32(header, 8, 0x00010000);
            WriteUInt32(header, 12, 92);
            WriteUInt32(header, 16, 0);
            WriteUInt32(header, 20, 0);
            WriteUInt64(header, 24, currentLba);
            WriteUInt64(header, 32, backupLba);
            WriteUInt64(header, 40, firstUsable);
            WriteUInt64(header, 48, lastUsable);
            diskGuid.ToByteArray().CopyTo(header, 56);
            WriteUInt64(header, 72, entriesLba);
            WriteUInt32(header, 80, count);
            WriteUInt32(header, 84, entrySize);
            WriteUInt32(header, 88, entryCrc32);

            byte[] headerPrefix = new byte[92];
            Buffer.BlockCopy(header, 0, headerPrefix, 0, 92);
            uint headerCrc = Crc32.ComputeWithExclusion(headerPrefix, 16, 4);
            WriteUInt32(header, 16, headerCrc);
            return header;
        }

        private static void WriteGptEntry(byte[] buffer, int offset, Guid partitionType, Guid partitionId, ulong firstLba, ulong lastLba, ulong attributes, string name)
        {
            partitionType.ToByteArray().CopyTo(buffer, offset);
            partitionId.ToByteArray().CopyTo(buffer, offset + 16);
            WriteUInt64(buffer, offset + 32, firstLba);
            WriteUInt64(buffer, offset + 40, lastLba);
            WriteUInt64(buffer, offset + 48, attributes);

            byte[] nameBytes = Encoding.Unicode.GetBytes(name);
            Array.Copy(nameBytes, 0, buffer, offset + 56, Math.Min(nameBytes.Length, 72));
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            WriteUInt32(buffer, offset, (uint)value);
            WriteUInt32(buffer, offset + 4, (uint)(value >> 32));
        }
    }
}
