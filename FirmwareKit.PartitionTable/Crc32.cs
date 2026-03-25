using System;

namespace FirmwareKit.PartitionTable
{
    public static class Crc32
    {
        private static readonly uint[] Table = new uint[256];
        private const uint Polynomial = 0xEDB88320;

        static Crc32()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc & 1) switch
                    {
                        1 => (crc >> 1) ^ Polynomial,
                        _ => crc >> 1,
                    };
                }
                Table[i] = crc;
            }
        }

        public static uint Compute(byte[] bytes, int offset = 0, int? count = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            int n = count ?? (bytes.Length - offset);
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + n; i++)
            {
                byte index = (byte)((crc & 0xFF) ^ bytes[i]);
                crc = (crc >> 8) ^ Table[index];
            }
            return ~crc;
        }

        public static uint ComputeWithExclusion(byte[] bytes, int excludeOffset, int excludeLength)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i >= excludeOffset && i < excludeOffset + excludeLength) continue;
                byte index = (byte)((crc & 0xFF) ^ bytes[i]);
                crc = (crc >> 8) ^ Table[index];
            }
            return ~crc;
        }
    }
}
