using FirmwareKit.PartitionTable.Exceptions;
using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using FirmwareKit.PartitionTable.Services;
using FirmwareKit.PartitionTable.Util;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace FirmwareKit.PartitionTable.Tests
{
    public class PartitionTableTests
    {
        private string TestGptPath => Path.Combine(AppContext.BaseDirectory, "test.gpt");
        private string TestGpt4096Path => Path.Combine(AppContext.BaseDirectory, "test-gpt-4096.bin");

        [Fact]
        public void Parse_GptTable_FromFile_MustSucceed()
        {
            Assert.True(File.Exists(TestGptPath), $"Test file not found: {TestGptPath}");

            using var fs = File.OpenRead(TestGptPath);
            long originalPosition = fs.Position;
            var table = PartitionTableReader.FromStream(fs, mutable: false);

            Assert.Equal(originalPosition, fs.Position);
            Assert.Equal(PartitionTableType.Gpt, table.Type);
            Assert.False(table.IsMutable);

            var gpt = Assert.IsType<GptPartitionTable>(table);
            Assert.NotEqual(Guid.Empty, gpt.DiskGuid);
            Assert.True(gpt.Partitions.Count > 0);
            Assert.True(gpt.SectorSize == 512 || gpt.SectorSize == 4096);
        }

        [Fact]
        public void Invalid_Mbr_Signature_Is_Rejected()
        {
            var image = new byte[512];

            using var ms = new MemoryStream(image);

            Assert.Throws<PartitionTableFormatException>(() => PartitionTableReader.FromStream(ms));
        }

        [Fact]
        public void Mbr_WithInvalidPartitionStatus_IsRejected()
        {
            var image = CreateEmptyMbrImage();
            image[446] = 0x7F;

            using var ms = new MemoryStream(image);

            Assert.Throws<PartitionTableFormatException>(() => PartitionTableReader.FromStream(ms));
        }

        [Fact]
        public void Parse_MbrTable_MutableFlagAndWriteReadback()
        {
            var mbr = CreateEmptyMbrImage();

            using var ms = new MemoryStream(mbr);
            var table = PartitionTableReader.FromStream(ms, mutable: true);
            Assert.Equal(PartitionTableType.Mbr, table.Type);
            Assert.True(table.IsMutable);

            var mbrTable = Assert.IsType<MbrPartitionTable>(table);
            var entry = new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 };
            mbrTable.SetPartition(0, entry);

            using var outStream = new MemoryStream(new byte[512]);
            table.WriteToStream(outStream);
            outStream.Position = 0;

            var reopened = PartitionTableReader.FromStream(outStream, mutable: false);
            Assert.Equal(PartitionTableType.Mbr, reopened.Type);
            var reopenedMbr = Assert.IsType<MbrPartitionTable>(reopened);
            Assert.Equal(0x07, reopenedMbr.Partitions[0].PartitionType);
            Assert.Equal((uint)2048, reopenedMbr.Partitions[0].FirstLba);
        }

        [Fact]
        public void ImmutableTable_ThrowsOnModification()
        {
            var mbr = CreateEmptyMbrImage();

            using var ms = new MemoryStream(mbr);
            var table = PartitionTableReader.FromStream(ms, mutable: false);
            Assert.False(table.IsMutable);

            var mbrTable = Assert.IsType<MbrPartitionTable>(table);
            Assert.Throws<PartitionOperationException>(() => mbrTable.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07 }));
        }

        [Fact]
        public void AutoDetects_Gpt_With_4096_SectorSize()
        {
            Assert.True(File.Exists(TestGpt4096Path), $"Test file not found: {TestGpt4096Path}");

            using var stream = File.OpenRead(TestGpt4096Path);
            var table = PartitionTableReader.FromStream(stream, mutable: false);

            var gpt = Assert.IsType<GptPartitionTable>(table);
            Assert.Equal(4096, gpt.SectorSize);
            Assert.Equal(PartitionTableType.Gpt, gpt.Type);
            Assert.True(gpt.IsEntryTableCrcValid, $"Entry CRC invalid. Header CRC: {gpt.IsHeaderCrcValid}");
            Assert.Single(gpt.Partitions);
        }

        [Fact]
        public void AutoDetects_Gpt_With_8192_SectorSize()
        {
            var image = CreateSampleGptImage(8192);

            using var stream = new MemoryStream(image);
            var table = PartitionTableReader.FromStream(stream, mutable: false);

            var gpt = Assert.IsType<GptPartitionTable>(table);
            Assert.Equal(8192, gpt.SectorSize);
            Assert.Equal(PartitionTableType.Gpt, gpt.Type);
            Assert.Single(gpt.Partitions);
        }

        [Fact]
        public void Invalid_Gpt_WithOverflowingEntryTableOffset_IsRejectedAsInvalidData()
        {
            var image = CreateSampleGptImage(512);

            image[510] = 0;
            image[511] = 0;

            byte[] overflowingOffset = BitConverter.GetBytes(ulong.MaxValue);
            Buffer.BlockCopy(overflowingOffset, 0, image, 512 + 72, overflowingOffset.Length);

            using var stream = new MemoryStream(image);

            Assert.Throws<PartitionTableFormatException>(() => PartitionTableReader.FromStream(stream, mutable: false));
        }

        [Fact]
        public void Parse_Gpt_WithPreferredSectorSize_8192_MustSucceed()
        {
            var image = CreateSampleGptImage(8192);

            using var stream = new MemoryStream(image);
            var table = PartitionTableReader.FromStream(stream, mutable: false, sectorSize: 8192);

            var gpt = Assert.IsType<GptPartitionTable>(table);
            Assert.Equal(8192, gpt.SectorSize);
            Assert.Single(gpt.Partitions);
        }

        [Fact]
        public void Parse_WithNonPositivePreferredSectorSize_Throws()
        {
            var image = CreateEmptyMbrImage();

            using var stream = new MemoryStream(image);
            Assert.Throws<ArgumentOutOfRangeException>(() => PartitionTableReader.FromStream(stream, mutable: false, sectorSize: 0));
        }

        [Theory]
        [InlineData(12, 600u)]
        [InlineData(84, 40u)]
        public void Invalid_Gpt_HeaderFields_AreRejected(int fieldOffset, uint value)
        {
            var image = CreateSampleGptImage(512);
            image[510] = 0;
            image[511] = 0;

            WriteUInt32(image, 512 + fieldOffset, value);

            using var stream = new MemoryStream(image);
            Assert.Throws<PartitionTableFormatException>(() => PartitionTableReader.FromStream(stream, mutable: false));
        }

        [Fact]
        public void Invalid_Gpt_WithOverflowingEntryTableLength_IsRejected()
        {
            var image = CreateSampleGptImage(512);
            image[510] = 0;
            image[511] = 0;

            WriteUInt32(image, 512 + 80, uint.MaxValue);
            WriteUInt32(image, 512 + 84, 128);

            using var stream = new MemoryStream(image);
            Assert.Throws<PartitionTableFormatException>(() => PartitionTableReader.FromStream(stream, mutable: false));
        }

        [Fact]
        public void WriteToFile_RoundTrips_Mbr()
        {
            var mbr = CreateEmptyMbrImage();

            using var source = new MemoryStream(mbr);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });

            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".img");
            try
            {
                table.WriteToFile(tempPath);
                Assert.True(File.Exists(tempPath));

                var reopened = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromFile(tempPath));
                Assert.Equal(0x07, reopened.Partitions[0].PartitionType);
                Assert.Equal((uint)2048, reopened.Partitions[0].FirstLba);
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
        public void WriteToStream_TruncatesExistingMbrTarget()
        {
            var mbr = CreateEmptyMbrImage();

            using var source = new MemoryStream(mbr);
            var table = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));
            table.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 });

            using var target = new MemoryStream(new byte[1024]);
            table.WriteToStream(target);

            Assert.Equal(512, target.Length);
            target.Position = 0;

            var reopened = Assert.IsType<MbrPartitionTable>(PartitionTableReader.FromStream(target, mutable: false));
            Assert.Equal(0x07, reopened.Partitions[0].PartitionType);
            Assert.Equal((uint)2048, reopened.Partitions[0].FirstLba);
        }

        [Fact]
        public void WriteToStream_TruncatesExistingGptTarget()
        {
            var image = File.ReadAllBytes(TestGptPath);

            using var source = new MemoryStream(image);
            var table = Assert.IsType<GptPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));

            using var target = new MemoryStream(new byte[512 * 140]);
            table.WriteToStream(target);

            long expectedLength = (long)(table.BackupLba + 1) * table.SectorSize;
            Assert.Equal(expectedLength, target.Length);
            target.Position = 0;

            var reopened = Assert.IsType<GptPartitionTable>(PartitionTableReader.FromStream(target, mutable: false));
            Assert.Equal(table.DiskGuid, reopened.DiskGuid);
            Assert.Equal(table.Partitions.Count, reopened.Partitions.Count);
            Assert.Equal(table.SectorSize, reopened.SectorSize);
        }

        [Fact]
        public void Crc32_ComputeWithExclusion_MatchesTwoSegments()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("abcdefghijklmnop");
            uint expected = Crc32.Compute(bytes, 0, 4);
            expected = Force.Crc32.Crc32Algorithm.Append(expected, bytes, 8, bytes.Length - 8);

            Assert.Equal(expected, Crc32.ComputeWithExclusion(bytes, 4, 4));
        }

        [Fact]
        public void ReadStream_PreservesPosition()
        {
            var mbr = CreateEmptyMbrImage();
            using var ms = new MemoryStream(mbr);
            ms.Position = 12;

            var table = PartitionTableReader.FromStream(ms);

            Assert.NotNull(table);
            Assert.Equal(12, ms.Position);
        }

        [Fact]
        public void Parse_AmlogicEpt_FromStream_MustSucceed()
        {
            byte[] image = CreateSampleAmlogicEptImage();

            using var stream = new MemoryStream(image);
            var table = PartitionTableReader.FromStream(stream, mutable: false);

            Assert.Equal(PartitionTableType.AmlogicEpt, table.Type);
            Assert.False(table.IsMutable);

            var ept = Assert.IsType<AmlogicPartitionTable>(table);
            Assert.True(ept.IsChecksumValid);
            Assert.Equal(3, ept.Partitions.Count);
            Assert.Equal("bootloader", ept.Partitions[0].Name);
        }

        [Fact]
        public void Parse_AmlogicEpt_MutableWriteReadback()
        {
            byte[] image = CreateSampleAmlogicEptImage();

            using var source = new MemoryStream(image);
            var table = Assert.IsType<AmlogicPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));
            table.UpdatePartition(1, new AmlogicPartitionEntry
            {
                Name = "reserved",
                Offset = 0x2400000,
                Size = 0x4800000,
                MaskFlags = 2
            });

            using var target = new MemoryStream(new byte[1304]);
            table.WriteToStream(target);
            target.Position = 0;

            var reopened = Assert.IsType<AmlogicPartitionTable>(PartitionTableReader.FromStream(target, mutable: false));
            Assert.True(reopened.IsChecksumValid);
            Assert.Equal((ulong)0x4800000, reopened.Partitions[1].Size);
            Assert.Equal((uint)2, reopened.Partitions[1].MaskFlags);
        }

        [Fact]
        public void Parse_AmlogicEpt_InvalidName_IsRejected()
        {
            byte[] image = CreateSampleAmlogicEptImage();
            image[24] = (byte)'!';
            WriteUInt32(image, 20, ComputeAmlogicStyleChecksum(image, 3));

            using var stream = new MemoryStream(image);

            Assert.Throws<PartitionTableFormatException>(() => PartitionTableReader.FromStream(stream, mutable: false));
        }

        [Fact]
        public void Parse_AmlogicEpt_EmptyTable_IsRejected()
        {
            byte[] image = CreateSampleAmlogicEptImage();
            WriteUInt32(image, 16, 0);
            WriteUInt32(image, 20, 0);

            using var stream = new MemoryStream(image);

            Assert.Throws<PartitionTableFormatException>(() => PartitionTableReader.FromStream(stream, mutable: false));
        }

        [Fact]
        public void WriteToStream_RejectsInvalidAmlogicPartitionName()
        {
            byte[] image = CreateSampleAmlogicEptImage();

            using var source = new MemoryStream(image);
            var table = Assert.IsType<AmlogicPartitionTable>(PartitionTableReader.FromStream(source, mutable: true));
            table.Partitions[0].Name = "bad!";

            using var target = new MemoryStream(new byte[1304]);

            Action write = () => table.WriteToStream(target);
            Assert.Throws<PartitionOperationException>(write);
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
            WriteGptEntry(entryBuffer, 0, Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"), Guid.Parse("11111111-2222-3333-4444-555555555555"), 2048, 4095, 0, "DATA");

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

        private static byte[] CreateSampleAmlogicEptImage()
        {
            byte[] image = new byte[1304];

            WriteUInt32(image, 0, 0x0054504D);
            WriteUInt32(image, 4, 0x302E3130);
            WriteUInt32(image, 8, 0x30302E30);
            WriteUInt32(image, 12, 0x00000000);
            WriteUInt32(image, 16, 3);

            WriteEptEntry(image, 0, "bootloader", 0x0000000000400000UL, 0x0000000000000000UL, 1);
            WriteEptEntry(image, 1, "reserved", 0x0000000004000000UL, 0x0000000002400000UL, 0);
            WriteEptEntry(image, 2, "env", 0x0000000000800000UL, 0x0000000006400000UL, 0);

            uint checksum = ComputeAmlogicStyleChecksum(image, 3);
            WriteUInt32(image, 20, checksum);
            return image;
        }

        private static void WriteEptEntry(byte[] buffer, int index, string name, ulong size, ulong offset, uint mask)
        {
            int entryOffset = 24 + (index * 40);
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            Array.Copy(nameBytes, 0, buffer, entryOffset, Math.Min(nameBytes.Length, 15));
            WriteUInt64(buffer, entryOffset + 16, size);
            WriteUInt64(buffer, entryOffset + 24, offset);
            WriteUInt32(buffer, entryOffset + 32, mask);
            WriteUInt32(buffer, entryOffset + 36, 0);
        }

        private static uint ComputeAmlogicStyleChecksum(byte[] tableBytes, int partitionsCount)
        {
            uint checksum = 0;
            for (int i = 0; i < partitionsCount; i++)
            {
                int cursor = 24;
                for (int j = 0; j < 10; j++)
                {
                    checksum += ReadUInt32(tableBytes, cursor);
                    cursor += 4;
                }
            }

            return checksum;
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));
        }
    }

    public class CustomExceptionTests
    {
        [Fact]
        public void PartitionTableFormatException_ContainsErrorCode()
        {
            var ex = new PartitionTableFormatException("test message", "TEST_CODE", PartitionTableType.Gpt);
            Assert.Equal("TEST_CODE", ex.ErrorCode);
            Assert.Equal(PartitionTableType.Gpt, ex.TableType);
            Assert.Equal("test message", ex.Message);
        }

        [Fact]
        public void PartitionTableChecksumException_ContainsChecksumKind()
        {
            var ex = new PartitionTableChecksumException("crc failed", "CRC_FAIL", "HeaderCrc", PartitionTableType.Gpt);
            Assert.Equal("HeaderCrc", ex.ChecksumKind);
            Assert.Equal(PartitionTableType.Gpt, ex.TableType);
        }

        [Fact]
        public void PartitionOperationException_ContainsPartitionIndex()
        {
            var ex = new PartitionOperationException("read-only", "TABLE_READ_ONLY", 2, PartitionTableType.Mbr);
            Assert.Equal(2, ex.PartitionIndex);
            Assert.Equal(PartitionTableType.Mbr, ex.TableType);
        }

        [Fact]
        public void PartitionTableRepairException_ContainsErrorCode()
        {
            var ex = new PartitionTableRepairException("repair failed", "REPAIR_FAIL", PartitionTableType.AmlogicEpt);
            Assert.Equal("REPAIR_FAIL", ex.ErrorCode);
            Assert.Equal(PartitionTableType.AmlogicEpt, ex.TableType);
        }

        [Fact]
        public void PartitionTableException_IsBaseClass()
        {
            Assert.True(typeof(PartitionTableFormatException).IsSubclassOf(typeof(PartitionTableException)));
            Assert.True(typeof(PartitionTableChecksumException).IsSubclassOf(typeof(PartitionTableException)));
            Assert.True(typeof(PartitionOperationException).IsSubclassOf(typeof(PartitionTableException)));
            Assert.True(typeof(PartitionTableRepairException).IsSubclassOf(typeof(PartitionTableException)));
        }
    }

    public class PartitionQueryBuilderTests
    {
        [Fact]
        public void FindGptPartitionByName_ReturnsMatchingPartition()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry
            {
                Name = "EFI",
                PartitionType = Guid.Parse("C12A7328-F81F-11D2-BA4B-00A0C93EC93B"),
                FirstLba = 2048,
                LastLba = 4095
            });

            var result = PartitionQuery.FindGptPartitionByName(table, "EFI");
            Assert.NotNull(result);
            Assert.Equal("EFI", result.Name);
        }

        [Fact]
        public void FindGptPartitionByName_ReturnsNullWhenNotFound()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            var result = PartitionQuery.FindGptPartitionByName(table, "NonExistent");
            Assert.Null(result);
        }

        [Fact]
        public void IndexOfGptPartitionByName_ReturnsCorrectIndex()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry { Name = "PartA", FirstLba = 2048, LastLba = 4095 });
            table.AddPartition(new GptPartitionEntry { Name = "PartB", FirstLba = 4096, LastLba = 8191 });

            Assert.Equal(0, PartitionQuery.IndexOfGptPartitionByName(table, "PartA"));
            Assert.Equal(1, PartitionQuery.IndexOfGptPartitionByName(table, "PartB"));
            Assert.Equal(-1, PartitionQuery.IndexOfGptPartitionByName(table, "PartC"));
        }

        [Fact]
        public void FindGptPartitionsByType_ReturnsAllMatching()
        {
            var efiType = Guid.Parse("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry { Name = "EFI1", PartitionType = efiType, FirstLba = 2048, LastLba = 4095 });
            table.AddPartition(new GptPartitionEntry { Name = "Root", PartitionType = Guid.Parse("0FC63DAF-8483-4772-8E79-3D69D8477DE4"), FirstLba = 4096, LastLba = 8191 });
            table.AddPartition(new GptPartitionEntry { Name = "EFI2", PartitionType = efiType, FirstLba = 8192, LastLba = 10239 });

            var results = PartitionQuery.FindGptPartitionsByType(table, efiType);
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void GetGptFreeRanges_ReturnsCorrectRanges()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry { Name = "A", FirstLba = 2048, LastLba = 4095 });
            table.AddPartition(new GptPartitionEntry { Name = "B", FirstLba = 8192, LastLba = 10239 });

            var freeRanges = PartitionQuery.GetGptFreeRanges(table);
            Assert.NotEmpty(freeRanges);
            Assert.All(freeRanges, r => Assert.True(r.FirstLba <= r.LastLba));
        }
    }

    public class PartitionTableBuilderTests
    {
        [Fact]
        public void CreateGpt_BuildsValidEmptyGpt()
        {
            var table = PartitionTableBuilder.CreateGpt().Build() as GptPartitionTable;
            Assert.NotNull(table);
            Assert.Equal(PartitionTableType.Gpt, table.Type);
            Assert.Equal(512, table.SectorSize);
        }

        [Fact]
        public void CreateGpt_WithCustomSectorSize_BuildsValidGpt()
        {
            var table = PartitionTableBuilder.CreateGpt()
                .WithSectorSize(4096)
                .Build() as GptPartitionTable;
            Assert.NotNull(table);
            Assert.Equal(4096, table.SectorSize);
        }

        [Fact]
        public void CreateGpt_WithCustomDiskGuid_PreservesGuid()
        {
            var guid = Guid.Parse("12345678-1234-1234-1234-123456789ABC");
            var table = PartitionTableBuilder.CreateGpt()
                .WithDiskGuid(guid)
                .Build() as GptPartitionTable;
            Assert.NotNull(table);
            Assert.Equal(guid, table.DiskGuid);
        }

        [Fact]
        public void CreateMbr_BuildsValidEmptyMbr()
        {
            var table = PartitionTableBuilder.CreateMbr().Build() as MbrPartitionTable;
            Assert.NotNull(table);
            Assert.Equal(PartitionTableType.Mbr, table.Type);
            Assert.Equal(4, table.Partitions.Length);
        }

        [Fact]
        public void CreateAmlogicEpt_BuildsValidAmlogicEptWithPartitions()
        {
            var table = PartitionTableBuilder.CreateAmlogicEpt().Build(mutable: true) as AmlogicPartitionTable;
            Assert.NotNull(table);
            Assert.Equal(PartitionTableType.AmlogicEpt, table.Type);
            table.AddPartition(new AmlogicPartitionEntry { Name = "boot", Offset = 0, Size = 4096 });
            Assert.Single(table.Partitions);
        }

        [Fact]
        public void CreateGpt_ThenAddPartition_RoundTrips()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry
            {
                Name = "TestPart",
                PartitionType = Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
                FirstLba = 2048,
                LastLba = 204800
            });

            using var ms = new MemoryStream();
            table.WriteToStream(ms);
            ms.Position = 0;
            var readBack = PartitionTableReader.FromStream(ms) as GptPartitionTable;
            Assert.NotNull(readBack);
            Assert.Single(readBack.Partitions);
            Assert.Equal("TestPart", readBack.Partitions[0].Name);
        }
    }

    public class GptBackupHeaderRecoveryTests
    {
        [Fact]
        public void Gpt_WithCorruptedPrimaryHeaderCrc_CanStillParse()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true) as GptPartitionTable;
            Assert.NotNull(table);
            table.AddPartition(new GptPartitionEntry
            {
                Name = "TestData",
                FirstLba = 2048,
                LastLba = 4095
            });

            using var ms = new MemoryStream();
            table.WriteToStream(ms);
            byte[] image = ms.ToArray();

            int crcOffset = 512 + 16;
            image[crcOffset] ^= 0xFF;
            image[crcOffset + 1] ^= 0xFF;
            image[crcOffset + 2] ^= 0xFF;
            image[crcOffset + 3] ^= 0xFF;

            using var readStream = new MemoryStream(image, writable: false);
            var recovered = PartitionTableReader.FromStream(readStream) as GptPartitionTable;
            Assert.NotNull(recovered);
            Assert.Equal(PartitionTableType.Gpt, recovered.Type);
            Assert.False(recovered.IsHeaderCrcValid);
        }
    }

    public class MbrOverlapDiagnosticsTests
    {
        [Fact]
        public void Mbr_WithOverlappingPartitions_ReportsOverlap()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: true) as MbrPartitionTable;
            Assert.NotNull(table);
            table.SetPartition(0, new MbrPartitionEntry { Status = 0x00, PartitionType = 0x07, FirstLba = 100, SectorCount = 200 });
            table.SetPartition(1, new MbrPartitionEntry { Status = 0x00, PartitionType = 0x07, FirstLba = 200, SectorCount = 200 });

            var report = PartitionTableDiagnostics.Analyze(table);
            Assert.Contains(report.Issues, i => i.Code == "MBR_OVERLAP");
        }

        [Fact]
        public void Mbr_WithNonOverlappingPartitions_NoOverlapIssue()
        {
            var table = PartitionTableBuilder.CreateMbr().Build(mutable: true) as MbrPartitionTable;
            Assert.NotNull(table);
            table.SetPartition(0, new MbrPartitionEntry { Status = 0x00, PartitionType = 0x07, FirstLba = 100, SectorCount = 100 });
            table.SetPartition(1, new MbrPartitionEntry { Status = 0x00, PartitionType = 0x07, FirstLba = 200, SectorCount = 100 });

            var report = PartitionTableDiagnostics.Analyze(table);
            Assert.DoesNotContain(report.Issues, i => i.Code == "MBR_OVERLAP");
        }
    }

    public class PartitionTableParserRegistryTests
    {
        [Fact]
        public void DefaultRegistry_ContainsThreeParsers()
        {
            var registry = PartitionTableParserRegistry.Default;
            Assert.Equal(3, registry.Parsers.Count);
        }

        [Fact]
        public void DefaultRegistry_ParsersInCorrectPriorityOrder()
        {
            var registry = PartitionTableParserRegistry.Default;
            Assert.Equal(PartitionTableType.AmlogicEpt, registry.Parsers[0].SupportedType);
            Assert.Equal(PartitionTableType.Gpt, registry.Parsers[1].SupportedType);
            Assert.Equal(PartitionTableType.Mbr, registry.Parsers[2].SupportedType);
        }

        [Fact]
        public void Registry_ParseGptImage_ReturnsGptTable()
        {
            var table = PartitionTableBuilder.CreateGpt().Build(mutable: true);
            using var ms = new MemoryStream();
            table.WriteToStream(ms);
            ms.Position = 0;

            var registry = new PartitionTableParserRegistry();
            registry.Register(new TestParser(PartitionTableType.Gpt, 0));
            var result = registry.TryParse(ms, mutable: false, sectorSize: 512);
            Assert.NotNull(result);
            Assert.Equal(PartitionTableType.Gpt, result.Type);
        }

        [Fact]
        public void Registry_Unregister_RemovesParser()
        {
            var registry = new PartitionTableParserRegistry();
            registry.Register(new TestParser(PartitionTableType.Gpt, 0));
            Assert.Single(registry.Parsers);
            Assert.True(registry.Unregister(PartitionTableType.Gpt));
            Assert.Empty(registry.Parsers);
        }

        [Fact]
        public void Registry_Clear_RemovesAllParsers()
        {
            var freshRegistry = new PartitionTableParserRegistry();
            freshRegistry.Register(new TestParser(PartitionTableType.AmlogicEpt, 0));
            freshRegistry.Register(new TestParser(PartitionTableType.Gpt, 1));
            Assert.Equal(2, freshRegistry.Parsers.Count);
            freshRegistry.Clear();
            Assert.Empty(freshRegistry.Parsers);
        }

        private sealed class TestParser : IPartitionTableParser
        {
            private readonly PartitionTableType _type;

            public TestParser(PartitionTableType type, int priority)
            {
                _type = type;
                Priority = priority;
            }

            public PartitionTableType SupportedType => _type;
            public int Priority { get; }

            public IPartitionTable? TryParse(Stream stream, bool mutable, int sectorSize)
            {
                return PartitionTableParser.FromStream(stream, mutable, sectorSize);
            }
        }
    }
}