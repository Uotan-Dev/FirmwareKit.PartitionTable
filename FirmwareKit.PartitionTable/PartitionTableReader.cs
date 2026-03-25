using System.IO;

namespace FirmwareKit.PartitionTable
{
    public static class PartitionTableReader
    {
        public static IPartitionTable FromStream(Stream stream, bool mutable = false)
        {
            return PartitionTableParser.FromStream(stream, mutable);
        }

        public static IPartitionTable FromFile(string path, bool mutable = false)
        {
            return PartitionTableParser.FromFile(path, mutable);
        }
    }
}
