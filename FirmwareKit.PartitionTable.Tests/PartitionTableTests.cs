using System;
using System.IO;
using FirmwareKit.PartitionTable;
using Xunit;

namespace FirmwareKit.PartitionTable.Tests
{
    public class PartitionTableTests
    {
        private string TestGptPath => Path.Combine(AppContext.BaseDirectory, "test.gpt");

        [Fact]
        public void Parse_GptTable_FromFile_MustSucceed()
        {
            Assert.True(File.Exists(TestGptPath), $"测试文件未找到：{TestGptPath}");
            using var fs = File.OpenRead(TestGptPath);
            var table = PartitionTableReader.FromStream(fs, mutable: false);

            Assert.Equal(PartitionTableType.Gpt, table.Type);
            Assert.False(table.IsMutable);
            Assert.IsType<PartitionTableParser.GptPartitionTable>(table);

            var gpt = (PartitionTableParser.GptPartitionTable)table;
            Assert.NotEqual(Guid.Empty, gpt.DiskGuid);
            Assert.True(gpt.Partitions.Count > 0);
        }

        [Fact]
        public void Parse_MbrTable_MutableFlagAndWriteReadback()
        {
            var mbr = new byte[512];
            mbr[510] = 0x55;
            mbr[511] = 0xAA;

            using var ms = new MemoryStream(mbr);
            var table = PartitionTableReader.FromStream(ms, mutable: true);
            Assert.Equal(PartitionTableType.Mbr, table.Type);
            Assert.True(table.IsMutable);

            var mbrTable = Assert.IsType<PartitionTableParser.MbrPartitionTable>(table);
            var entry = new MbrPartitionEntry { PartitionType = 0x07, FirstLba = 2048, SectorCount = 1024 };
            mbrTable.SetPartition(0, entry);

            using var outStream = new MemoryStream(new byte[512]);
            table.WriteToStream(outStream);
            outStream.Position = 0;

            var reopened = PartitionTableReader.FromStream(outStream, mutable: false);
            Assert.Equal(PartitionTableType.Mbr, reopened.Type);
            var reopenedMbr = Assert.IsType<PartitionTableParser.MbrPartitionTable>(reopened);
            Assert.Equal(0x07, reopenedMbr.Partitions[0].PartitionType);
            Assert.Equal((uint)2048, reopenedMbr.Partitions[0].FirstLba);
        }

        [Fact]
        public void ImmutableTable_ThrowsOnModification()
        {
            var mbr = new byte[512];
            mbr[510] = 0x55;
            mbr[511] = 0xAA;

            using var ms = new MemoryStream(mbr);
            var table = PartitionTableReader.FromStream(ms, mutable: false);
            Assert.False(table.IsMutable);
            var mbrTable = Assert.IsType<PartitionTableParser.MbrPartitionTable>(table);
            Assert.Throws<InvalidOperationException>(() => mbrTable.SetPartition(0, new MbrPartitionEntry { PartitionType = 0x07 }));
        }
    }
}
