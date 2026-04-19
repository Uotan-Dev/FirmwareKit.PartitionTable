using System;
using System.IO;

namespace FirmwareKit.PartitionTable.Util
{
    /// <summary>
    /// Normalizes platform-specific disk or image paths.
    /// 标准化平台相关的磁盘或镜像路径。
    /// </summary>
    public static class DevicePath
    {
        /// <summary>
        /// Normalizes a raw device path or regular file path.
        /// 标准化原始设备路径或普通文件路径。
        /// </summary>
        /// <param name="path">Input path string. / 输入路径字符串。</param>
        /// <returns>Normalized path. / 标准化后的路径。</returns>
        public static string Normalize(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string trimmed = path.Trim();
            if (trimmed.Length == 0) throw new ArgumentException("Path must not be empty.", nameof(path));

            if (trimmed.StartsWith("\\\\.\\", StringComparison.Ordinal))
            {
                return trimmed;
            }

            if (trimmed.StartsWith("/dev/", StringComparison.Ordinal))
            {
                return trimmed;
            }

            return Path.GetFullPath(trimmed);
        }
    }
}
