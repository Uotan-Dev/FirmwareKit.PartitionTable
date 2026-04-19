using System;

namespace FirmwareKit.PartitionTable.Util
{
    /// <summary>
    /// CRC-32 helper backed by the <c>Crc32.NET</c> package.
    /// 基于 <c>Crc32.NET</c> 包的 CRC-32 辅助类。
    /// </summary>
    public static class Crc32
    {
        /// <summary>
        /// Computes the CRC-32 of a byte array segment.
        /// 计算字节数组片段的 CRC-32。
        /// </summary>
        /// <param name="bytes">The input buffer.</param>
        /// <param name="offset">The start offset.</param>
        /// <param name="count">The number of bytes to include. When omitted, the remainder of the buffer is used.</param>
        /// <returns>The CRC-32 value.</returns>
        public static uint Compute(byte[] bytes, int offset = 0, int? count = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || offset > bytes.Length) throw new ArgumentOutOfRangeException(nameof(offset));

            int length = count ?? (bytes.Length - offset);
            if (length < 0 || offset + length > bytes.Length) throw new ArgumentOutOfRangeException(nameof(count));

            return Force.Crc32.Crc32Algorithm.Compute(bytes, offset, length);
        }

        /// <summary>
        /// Computes the CRC-32 of a byte array while skipping a contiguous range.
        /// 计算字节数组在跳过连续区间后的 CRC-32。
        /// </summary>
        /// <param name="bytes">The input buffer.</param>
        /// <param name="excludeOffset">The start offset of the excluded range.</param>
        /// <param name="excludeLength">The length of the excluded range.</param>
        /// <returns>The CRC-32 value.</returns>
        public static uint ComputeWithExclusion(byte[] bytes, int excludeOffset, int excludeLength)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (excludeOffset < 0 || excludeOffset > bytes.Length) throw new ArgumentOutOfRangeException(nameof(excludeOffset));
            if (excludeLength < 0 || excludeOffset + excludeLength > bytes.Length) throw new ArgumentOutOfRangeException(nameof(excludeLength));

            uint crc = 0;
            if (excludeOffset > 0)
            {
                crc = Force.Crc32.Crc32Algorithm.Append(crc, bytes, 0, excludeOffset);
            }

            int tailOffset = excludeOffset + excludeLength;
            int tailLength = bytes.Length - tailOffset;
            if (tailLength > 0)
            {
                crc = Force.Crc32.Crc32Algorithm.Append(crc, bytes, tailOffset, tailLength);
            }

            return crc;
        }

        /// <summary>
        /// Computes the CRC-32 of the buffer and writes the checksum to the end of the array.
        /// 计算缓冲区的 CRC-32，并将校验值写到数组末尾。
        /// </summary>
        /// <param name="input">The buffer that will receive the checksum.</param>
        /// <returns>The computed CRC-32 value.</returns>
        public static uint ComputeAndWriteToEnd(byte[] input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return Force.Crc32.Crc32Algorithm.ComputeAndWriteToEnd(input);
        }

        /// <summary>
        /// Computes the CRC-32 of a buffer segment and writes the checksum to the end of the segment.
        /// 计算缓冲区片段的 CRC-32，并将校验值写到片段末尾。
        /// </summary>
        /// <param name="input">The buffer that will receive the checksum.</param>
        /// <param name="offset">The start offset of the data segment.</param>
        /// <param name="length">The length of the data segment.</param>
        /// <returns>The computed CRC-32 value.</returns>
        public static uint ComputeAndWriteToEnd(byte[] input, int offset, int length)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return Force.Crc32.Crc32Algorithm.ComputeAndWriteToEnd(input, offset, length);
        }

        /// <summary>
        /// Validates a buffer that stores the CRC-32 at the end of the array.
        /// 验证数组末尾是否存放了有效的 CRC-32。
        /// </summary>
        /// <param name="input">The input buffer.</param>
        /// <returns><see langword="true" /> when the checksum is valid.</returns>
        public static bool IsValidWithCrcAtEnd(byte[] input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return Force.Crc32.Crc32Algorithm.IsValidWithCrcAtEnd(input);
        }

        /// <summary>
        /// Validates a buffer segment that stores the CRC-32 at the end of the segment.
        /// 验证缓冲区片段末尾是否存放了有效的 CRC-32。
        /// </summary>
        /// <param name="input">The input buffer.</param>
        /// <param name="offset">The start offset of the data segment.</param>
        /// <param name="lengthWithCrc">The length of the data segment including the checksum.</param>
        /// <returns><see langword="true" /> when the checksum is valid.</returns>
        public static bool IsValidWithCrcAtEnd(byte[] input, int offset, int lengthWithCrc)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return Force.Crc32.Crc32Algorithm.IsValidWithCrcAtEnd(input, offset, lengthWithCrc);
        }
    }
}
