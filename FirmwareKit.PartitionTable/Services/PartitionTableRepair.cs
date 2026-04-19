using FirmwareKit.PartitionTable.Models;
using System;
using System.IO;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Provides conservative repair utilities for partition tables.
    /// 提供保守的分区表修复工具。
    /// </summary>
    public static class PartitionTableRepair
    {
        /// <summary>
        /// Repairs GPT CRC-related metadata in-place on a writable stream.
        /// 在可写流上就地修复 GPT 的 CRC 相关元数据。
        /// </summary>
        /// <param name="stream">The target stream. / 目标流。</param>
        /// <param name="sectorSize">Preferred sector size, or <see langword="null" /> to auto-detect. / 首选扇区大小，传 <see langword="null" /> 时自动检测。</param>
        /// <returns>The repair outcome and performed actions. / 修复结果及执行动作。</returns>
        public static PartitionRepairResult RepairGptCrcInPlace(Stream stream, int? sectorSize = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek || !stream.CanWrite) throw new NotSupportedException("The stream must be readable, writable, and seekable.");

            var result = new PartitionRepairResult();
            var table = PartitionTableReader.FromStream(stream, mutable: true, sectorSize: sectorSize);
            if (table is not GptPartitionTable gpt)
            {
                result.Actions.Add("No GPT table was found; nothing repaired.");
                return result;
            }

            var report = PartitionTableDiagnostics.Analyze(gpt);
            gpt.WriteToStream(stream);
            result.Repaired = true;
            result.Actions.Add(report.IsHealthy
                ? "Refreshed GPT headers and entry arrays."
                : "Rewrote GPT headers and entry arrays to repair detected issues.");
            return result;
        }

        /// <summary>
        /// Repairs a missing MBR signature when the partition entries are otherwise valid.
        /// 在 MBR 分区项有效但签名缺失时进行修复。
        /// </summary>
        /// <param name="stream">The target stream. / 目标流。</param>
        /// <returns>The repair outcome. / 修复结果。</returns>
        public static PartitionRepairResult RepairMbrSignatureInPlace(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek || !stream.CanWrite) throw new NotSupportedException("The stream must be readable, writable, and seekable.");

            var result = new PartitionRepairResult();
            long originalPosition = stream.Position;
            try
            {
                if (stream.Length < 512)
                {
                    result.Actions.Add("Stream is too small to contain an MBR.");
                    return result;
                }

                byte[] buffer = new byte[512];
                stream.Position = 0;
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read < buffer.Length)
                {
                    result.Actions.Add("Failed to read a complete MBR sector.");
                    return result;
                }

                ushort signature = (ushort)(buffer[510] | (buffer[511] << 8));
                if (signature == 0xAA55)
                {
                    result.Actions.Add("MBR signature already valid.");
                    return result;
                }

                for (int i = 0; i < 4; i++)
                {
                    int offset = 446 + (16 * i);
                    byte status = buffer[offset];
                    if (status != 0x00 && status != 0x80)
                    {
                        result.Actions.Add("MBR partition status bytes are not valid enough for automatic signature repair.");
                        return result;
                    }
                }

                buffer[510] = 0x55;
                buffer[511] = 0xAA;
                stream.Position = 0;
                stream.Write(buffer, 0, buffer.Length);
                stream.Flush();
                result.Repaired = true;
                result.Actions.Add("Restored the MBR boot signature (0x55AA).");
                return result;
            }
            finally
            {
                stream.Position = Math.Min(originalPosition, stream.Length);
            }
        }

        /// <summary>
        /// Repairs an Amlogic EPT checksum in-place by rewriting the table.
        /// 通过重写表修复 Amlogic EPT 校验和。
        /// </summary>
        /// <param name="stream">The target stream. / 目标流。</param>
        /// <returns>The repair outcome. / 修复结果。</returns>
        public static PartitionRepairResult RepairAmlogicChecksumInPlace(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek || !stream.CanWrite) throw new NotSupportedException("The stream must be readable, writable, and seekable.");

            var result = new PartitionRepairResult();
            var table = PartitionTableReader.FromStream(stream, mutable: true);
            if (table is not AmlogicPartitionTable amlogic)
            {
                result.Actions.Add("No Amlogic EPT table was found; nothing repaired.");
                return result;
            }

            amlogic.WriteToStream(stream);
            result.Repaired = true;
            result.Actions.Add("Rewrote Amlogic EPT checksum and table contents.");
            return result;
        }

        /// <summary>
        /// Repairs the first recognizable table type in-place.
        /// 就地修复首个可识别的分区表类型。
        /// </summary>
        /// <param name="stream">The target stream. / 目标流。</param>
        /// <param name="sectorSize">Preferred GPT sector size, or <see langword="null" /> to auto-detect. / GPT 首选扇区大小，或传 <see langword="null" /> 自动检测。</param>
        /// <returns>The repair outcome. / 修复结果。</returns>
        public static PartitionRepairResult RepairAnyInPlace(Stream stream, int? sectorSize = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek || !stream.CanWrite) throw new NotSupportedException("The stream must be readable, writable, and seekable.");

            var signatureRepair = RepairMbrSignatureInPlace(stream);
            if (signatureRepair.Repaired)
            {
                stream.Position = 0;
            }

            try
            {
                var table = PartitionTableReader.FromStream(stream, mutable: true, sectorSize: sectorSize);
                if (table is GptPartitionTable)
                {
                    stream.Position = 0;
                    return RepairGptCrcInPlace(stream, sectorSize);
                }

                if (table is AmlogicPartitionTable)
                {
                    stream.Position = 0;
                    return RepairAmlogicChecksumInPlace(stream);
                }

                if (table is MbrPartitionTable mbr)
                {
                    mbr.WriteToStream(stream);
                    var result = new PartitionRepairResult { Repaired = true };
                    result.Actions.Add(signatureRepair.Repaired ? "Repaired MBR signature and normalized table contents." : "Normalized MBR table contents.");
                    return result;
                }
            }
            catch (InvalidDataException)
            {
                if (signatureRepair.Repaired)
                {
                    stream.Position = 0;
                    var table = PartitionTableReader.FromStream(stream, mutable: true, sectorSize: sectorSize);
                    if (table is MbrPartitionTable mbr)
                    {
                        mbr.WriteToStream(stream);
                        var result = new PartitionRepairResult { Repaired = true };
                        result.Actions.Add("Repaired MBR signature and normalized table contents.");
                        return result;
                    }
                }
            }

            if (signatureRepair.Actions.Count == 0)
            {
                signatureRepair.Actions.Add("No recognizable partition table was found; nothing repaired.");
            }

            return signatureRepair;
        }

        /// <summary>
        /// Repairs GPT CRC-related metadata in a file.
        /// 修复文件中的 GPT CRC 相关元数据。
        /// </summary>
        /// <param name="path">The image file path. / 镜像文件路径。</param>
        /// <param name="sectorSize">Preferred sector size, or <see langword="null" /> to auto-detect. / 首选扇区大小，传 <see langword="null" /> 时自动检测。</param>
        /// <returns>The repair outcome and performed actions. / 修复结果及执行动作。</returns>
        public static PartitionRepairResult RepairGptCrcInFile(string path, int? sectorSize = null)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return RepairGptCrcInPlace(stream, sectorSize);
        }

        /// <summary>
        /// Repairs any recognizable partition table in a file.
        /// 修复文件中的任意可识别分区表。
        /// </summary>
        /// <param name="path">The image file path. / 镜像文件路径。</param>
        /// <param name="sectorSize">Preferred GPT sector size, or <see langword="null" /> to auto-detect. / GPT 首选扇区大小，或传 <see langword="null" /> 自动检测。</param>
        /// <returns>The repair outcome and performed actions. / 修复结果及执行动作。</returns>
        public static PartitionRepairResult RepairAnyInFile(string path, int? sectorSize = null)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return RepairAnyInPlace(stream, sectorSize);
        }
    }
}
