using System;
using System.IO;

namespace FirmwareKit.PartitionTable
{
    internal static class StreamExtensions
    {
        public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unable to read enough data from the stream.");
                }

                totalRead += read;
            }
        }

        public static void WriteExactly(this Stream stream, byte[] buffer, int offset, int count)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            stream.Write(buffer, offset, count);
        }
    }
}
