using FirmwareKit.PartitionTable.Exceptions;
using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using System;
using System.Collections.Generic;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Pre-write validation that checks partition table integrity before writing.
    /// 写入前验证，在写入前检查分区表完整性。
    /// </summary>
    public static class PartitionTableValidator
    {
        /// <summary>
        /// Validates a partition table and returns a report of issues that would prevent a safe write.
        /// 验证分区表并返回会阻止安全写入的问题报告。
        /// </summary>
        /// <param name="table">The partition table to validate. / 待验证的分区表。</param>
        /// <returns>A validation result containing any errors or warnings. / 包含错误或警告的验证结果。</returns>
        public static PartitionValidationResult Validate(IPartitionTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var result = new PartitionValidationResult { Type = table.Type };

            if (!table.IsMutable)
            {
                result.Errors.Add(new PartitionValidationIssue
                {
                    Code = "TABLE_READ_ONLY",
                    Message = "The partition table is read-only and cannot be written.",
                    Severity = PartitionIssueSeverity.Error
                });
                return result;
            }

            if (table is GptPartitionTable gpt)
            {
                ValidateGpt(gpt, result);
            }
            else if (table is MbrPartitionTable mbr)
            {
                ValidateMbr(mbr, result);
            }
            else if (table is AmlogicPartitionTable amlogic)
            {
                ValidateAmlogic(amlogic, result);
            }

            return result;
        }

        /// <summary>
        /// Validates and throws if any error-level issues are found.
        /// 验证并在发现错误级问题时抛出异常。
        /// </summary>
        /// <param name="table">The partition table to validate. / 待验证的分区表。</param>
        public static void ValidateAndThrow(IPartitionTable table)
        {
            var result = Validate(table);
            if (!result.IsValid)
            {
                throw new PartitionOperationException(
                    $"Partition table validation failed with {result.Errors.Count} error(s).",
                    "VALIDATION_FAILED",
                    tableType: table.Type);
            }
        }

        private static void ValidateGpt(GptPartitionTable table, PartitionValidationResult result)
        {
            if (table.PartitionEntrySize == 0 || table.PartitionsCount == 0)
            {
                result.Errors.Add(new PartitionValidationIssue
                {
                    Code = "GPT_HEADER_INVALID",
                    Message = "GPT header has invalid partition entry size or count.",
                    Severity = PartitionIssueSeverity.Error
                });
            }

            if (table.BackupLba <= 0)
            {
                result.Errors.Add(new PartitionValidationIssue
                {
                    Code = "GPT_BACKUP_LBA_INVALID",
                    Message = "GPT backup LBA is invalid.",
                    Severity = PartitionIssueSeverity.Error
                });
            }

            if (table.Partitions.Count > table.PartitionsCount)
            {
                result.Errors.Add(new PartitionValidationIssue
                {
                    Code = "GPT_PARTITION_COUNT_EXCEEDED",
                    Message = $"GPT partition count ({table.Partitions.Count}) exceeds the maximum ({table.PartitionsCount}).",
                    Severity = PartitionIssueSeverity.Error
                });
            }

            var intervals = new List<(ulong Start, ulong End)>();
            for (int i = 0; i < table.Partitions.Count; i++)
            {
                GptPartitionEntry partition = table.Partitions[i];
                if (partition.IsEmpty) continue;

                if (partition.FirstLba > partition.LastLba)
                {
                    result.Errors.Add(new PartitionValidationIssue
                    {
                        Code = "GPT_LBA_ORDER",
                        Message = $"GPT partition at index {i} has FirstLba > LastLba.",
                        Severity = PartitionIssueSeverity.Error,
                        PartitionIndex = i
                    });
                    continue;
                }

                if (partition.FirstLba < table.FirstUsableLba)
                {
                    result.Warnings.Add(new PartitionValidationIssue
                    {
                        Code = "GPT_LBA_BELOW_USABLE",
                        Message = $"GPT partition at index {i} starts below the first usable LBA.",
                        Severity = PartitionIssueSeverity.Warning,
                        PartitionIndex = i
                    });
                }

                if (partition.LastLba > table.LastUsableLba)
                {
                    result.Warnings.Add(new PartitionValidationIssue
                    {
                        Code = "GPT_LBA_ABOVE_USABLE",
                        Message = $"GPT partition at index {i} extends beyond the last usable LBA.",
                        Severity = PartitionIssueSeverity.Warning,
                        PartitionIndex = i
                    });
                }

                if (partition.PartitionType == Guid.Empty)
                {
                    result.Warnings.Add(new PartitionValidationIssue
                    {
                        Code = "GPT_EMPTY_TYPE_GUID",
                        Message = $"GPT partition at index {i} has an empty partition type GUID.",
                        Severity = PartitionIssueSeverity.Warning,
                        PartitionIndex = i
                    });
                }

                intervals.Add((partition.FirstLba, partition.LastLba));
            }

            CheckOverlaps(intervals, result, "GPT_OVERLAP");
        }

        private static void ValidateMbr(MbrPartitionTable table, PartitionValidationResult result)
        {
            MbrPartitionEntry[] partitions = table.Partitions;
            bool hasProtective = false;
            bool hasRegular = false;

            for (int i = 0; i < partitions.Length; i++)
            {
                MbrPartitionEntry entry = partitions[i];
                if (entry.IsEmpty) continue;

                if (entry.Status != 0x00 && entry.Status != 0x80)
                {
                    result.Errors.Add(new PartitionValidationIssue
                    {
                        Code = "MBR_STATUS_INVALID",
                        Message = $"MBR partition at index {i} has invalid status byte 0x{entry.Status:X2}.",
                        Severity = PartitionIssueSeverity.Error,
                        PartitionIndex = i
                    });
                }

                if (entry.PartitionType == 0xEE)
                {
                    hasProtective = true;
                }
                else
                {
                    hasRegular = true;
                }
            }

            if (hasProtective && hasRegular)
            {
                result.Warnings.Add(new PartitionValidationIssue
                {
                    Code = "MBR_HYBRID",
                    Message = "Hybrid MBR detected (protective and regular partitions mixed).",
                    Severity = PartitionIssueSeverity.Warning
                });
            }

            var intervals = new List<(ulong Start, ulong End)>();
            for (int i = 0; i < partitions.Length; i++)
            {
                MbrPartitionEntry entry = partitions[i];
                if (entry.IsEmpty || entry.SectorCount == 0) continue;

                ulong end;
                try
                {
                    end = checked((ulong)entry.FirstLba + entry.SectorCount - 1);
                }
                catch (OverflowException)
                {
                    result.Errors.Add(new PartitionValidationIssue
                    {
                        Code = "MBR_LBA_OVERFLOW",
                        Message = $"MBR partition at index {i} has an overflowing LBA range.",
                        Severity = PartitionIssueSeverity.Error,
                        PartitionIndex = i
                    });
                    continue;
                }

                intervals.Add((entry.FirstLba, end));
            }

            CheckOverlaps(intervals, result, "MBR_OVERLAP");
        }

        private static void ValidateAmlogic(AmlogicPartitionTable table, PartitionValidationResult result)
        {
            if (table.Partitions.Count == 0)
            {
                result.Errors.Add(new PartitionValidationIssue
                {
                    Code = "AMLOGIC_EPT_EMPTY",
                    Message = "Amlogic EPT must contain at least one partition.",
                    Severity = PartitionIssueSeverity.Error
                });
            }

            if (table.Partitions.Count > AmlogicPartitionTableSupport.PartitionSlotCount)
            {
                result.Errors.Add(new PartitionValidationIssue
                {
                    Code = "AMLOGIC_EPT_COUNT_EXCEEDED",
                    Message = $"Amlogic EPT partition count ({table.Partitions.Count}) exceeds the maximum ({AmlogicPartitionTableSupport.PartitionSlotCount}).",
                    Severity = PartitionIssueSeverity.Error
                });
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var intervals = new List<(ulong Start, ulong End)>();

            for (int i = 0; i < table.Partitions.Count; i++)
            {
                AmlogicPartitionEntry partition = table.Partitions[i];

                if (string.IsNullOrWhiteSpace(partition.Name))
                {
                    result.Errors.Add(new PartitionValidationIssue
                    {
                        Code = "AMLOGIC_EPT_NAME_EMPTY",
                        Message = $"Amlogic EPT partition at index {i} has an empty name.",
                        Severity = PartitionIssueSeverity.Error,
                        PartitionIndex = i
                    });
                }
                else if (!AmlogicPartitionTableSupport.IsValidPartitionName(partition.Name))
                {
                    result.Errors.Add(new PartitionValidationIssue
                    {
                        Code = "AMLOGIC_EPT_NAME_INVALID",
                        Message = $"Amlogic EPT partition at index {i} has an invalid name: '{partition.Name}'.",
                        Severity = PartitionIssueSeverity.Error,
                        PartitionIndex = i
                    });
                }

                if (!names.Add(partition.Name))
                {
                    result.Errors.Add(new PartitionValidationIssue
                    {
                        Code = "AMLOGIC_EPT_NAME_DUP",
                        Message = $"Amlogic EPT partition at index {i} has a duplicate name: '{partition.Name}'.",
                        Severity = PartitionIssueSeverity.Error,
                        PartitionIndex = i
                    });
                }

                if (partition.Size == 0) continue;

                ulong end;
                try
                {
                    end = checked(partition.Offset + partition.Size - 1);
                }
                catch (OverflowException)
                {
                    result.Errors.Add(new PartitionValidationIssue
                    {
                        Code = "AMLOGIC_EPT_RANGE_OVERFLOW",
                        Message = $"Amlogic EPT partition at index {i} has an overflowing byte range.",
                        Severity = PartitionIssueSeverity.Error,
                        PartitionIndex = i
                    });
                    continue;
                }

                intervals.Add((partition.Offset, end));
            }

            CheckOverlaps(intervals, result, "AMLOGIC_EPT_OVERLAP");
        }

        private static void CheckOverlaps(List<(ulong Start, ulong End)> intervals, PartitionValidationResult result, string errorCode)
        {
            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = 1; i < intervals.Count; i++)
            {
                if (intervals[i - 1].End >= intervals[i].Start)
                {
                    result.Errors.Add(new PartitionValidationIssue
                    {
                        Code = errorCode,
                        Message = "Partitions contain overlapping ranges.",
                        Severity = PartitionIssueSeverity.Error
                    });
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Result of a partition table validation operation.
    /// 分区表验证操作的结果。
    /// </summary>
    public sealed class PartitionValidationResult
    {
        /// <summary>
        /// Gets or sets the partition table type that was validated.
        /// 获取或设置被验证的分区表类型。
        /// </summary>
        public PartitionTableType Type { get; set; }

        /// <summary>
        /// Gets the error-level validation issues.
        /// 获取错误级别的验证问题。
        /// </summary>
        public List<PartitionValidationIssue> Errors { get; } = new List<PartitionValidationIssue>();

        /// <summary>
        /// Gets the warning-level validation issues.
        /// 获取警告级别的验证问题。
        /// </summary>
        public List<PartitionValidationIssue> Warnings { get; } = new List<PartitionValidationIssue>();

        /// <summary>
        /// Gets a value indicating whether the table has no error-level issues.
        /// 获取表是否没有错误级别的问题。
        /// </summary>
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// One issue found during partition table validation.
    /// 分区表验证期间发现的一个问题。
    /// </summary>
    public sealed class PartitionValidationIssue
    {
        /// <summary>
        /// Gets or sets the machine-readable issue code.
        /// 获取或设置机器可读的问题代码。
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the human-readable issue message.
        /// 获取或设置人类可读的问题描述。
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the issue severity level.
        /// 获取或设置问题严重级别。
        /// </summary>
        public PartitionIssueSeverity Severity { get; set; }

        /// <summary>
        /// Gets or sets the zero-based partition index, if applicable.
        /// 获取或设置零基分区索引（如适用）。
        /// </summary>
        public int? PartitionIndex { get; set; }
    }
}
