using System;
using System.IO;
using System.Text;
using FirmwareKit.PartitionTable;
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

            Assert.Throws<InvalidDataException>(() => PartitionTableReader.FromStream(ms));
        }

        [Fact]
        public void Mbr_WithInvalidPartitionStatus_IsRejected()
        {
            var image = CreateEmptyMbrImage();
            image[446] = 0x7F;

            using var ms = new MemoryStream(image);

            Assert.Throws<InvalidDataException>(() => PartitionTableReader.FromStream(ms));
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
            Assert.Throws<InvalidOperationException>(() => mbrTable.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07 }));
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

            Assert.Throws<InvalidDataException>(() => PartitionTableReader.FromStream(stream, mutable: false));
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
            Assert.Throws<InvalidDataException>(() => PartitionTableReader.FromStream(stream, mutable: false));
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
            Assert.Throws<InvalidDataException>(() => PartitionTableReader.FromStream(stream, mutable: false));
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
    }
}