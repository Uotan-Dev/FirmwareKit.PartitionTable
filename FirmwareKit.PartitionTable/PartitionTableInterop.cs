using System;
using System.Collections.Generic;
using System.IO;

namespace FirmwareKit.PartitionTable
{
    public enum PartitionTableType
    {
        Mbr,
        Gpt
    }

    public interface IPartitionTable
    {
        PartitionTableType Type { get; }

        bool IsMutable { get; }

        void EnsureMutable();

        void WriteToStream(Stream stream);

        void WriteToFile(string path);
    }

    public interface IMutablePartitionTable : IPartitionTable
    {
        void SetMutable(bool mutable);
    }

    public sealed class MbrPartitionEntry
    {
        public byte Status { get; set; }
        public byte[] FirstCHS { get; set; } = new byte[3];
        public byte PartitionType { get; set; }
        public byte[] LastCHS { get; set; } = new byte[3];
        public uint FirstLba { get; set; }
        public uint SectorCount { get; set; }

        public bool IsEmpty => PartitionType == 0 && Status == 0 && FirstLba == 0 && SectorCount == 0;
    }

    public sealed class GptPartitionEntry
    {
        public Guid PartitionType { get; set; }
        public Guid PartitionId { get; set; }
        public ulong FirstLba { get; set; }
        public ulong LastLba { get; set; }
        public ulong Attributes { get; set; }
        public string Name { get; set; } = string.Empty;

        public bool IsEmpty => PartitionType == Guid.Empty;
    }
}
