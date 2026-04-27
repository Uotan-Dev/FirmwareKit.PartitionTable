using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using System;
using System.Collections.Generic;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Validates parsed partition tables and emits diagnostic issues.
    /// 校验已解析分区表并输出诊断问题。
    /// </summary>
    public static class PartitionTableDiagnostics
    {
        /// <summary>
        /// Analyzes a partition table and returns structured diagnostic issues.
        /// 分析分区表并返回结构化诊断问题。
        /// </summary>
        /// <param name="table">The partition table to analyze. / 待分析的分区表。</param>
        /// <returns>The diagnostic report. / 诊断报告。</returns>
        public static PartitionDiagnosticsReport Analyze(IPartitionTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var report = new PartitionDiagnosticsReport { Type = table.Type };
            if (table is GptPartitionTable gpt)
            {
                AnalyzeGpt(gpt, report);
            }
            else if (table is MbrPartitionTable mbr)
            {
                AnalyzeMbr(mbr, report);
            }
            else if (table is AmlogicPartitionTable amlogic)
            {
                AnalyzeAmlogicEpt(amlogic, report);
            }

            return report;
        }

        private static void AnalyzeGpt(GptPartitionTable table, PartitionDiagnosticsReport report)
        {
            if (!table.IsHeaderCrcValid)
            {
                report.Issues.Add(new PartitionDiagnosticIssue
                {
                    Code = "GPT_HEADER_CRC",
                    Message = table.IsRecoveredFromBackup
                        ? "GPT primary header CRC mismatch; table recovered from backup header."
                        : "GPT primary header CRC mismatch.",
                    Severity = table.IsRecoveredFromBackup ? PartitionIssueSeverity.Warning : PartitionIssueSeverity.Error,
                    CanAutoRepair = true
                });
            }

            if (!table.IsEntryTableCrcValid)
            {
                report.Issues.Add(new PartitionDiagnosticIssue
                {
                    Code = "GPT_ENTRY_CRC",
                    Message = "GPT partition entry array CRC mismatch.",
                    Severity = PartitionIssueSeverity.Error,
                    CanAutoRepair = true
                });
            }

            if (table.Partitions.Count > table.PartitionsCount)
            {
                report.Issues.Add(new PartitionDiagnosticIssue
                {
                    Code = "GPT_PARTITION_COUNT",
                    Message = "Parsed partition count exceeds GPT entry limit.",
                    Severity = PartitionIssueSeverity.Error,
                    CanAutoRepair = false
                });
            }

            var intervals = new List<(ulong Start, ulong End)>();
            for (int i = 0; i < table.Partitions.Count; i++)
            {
                GptPartitionEntry partition = table.Partitions[i];
                if (partition.FirstLba > partition.LastLba)
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "GPT_LBA_ORDER",
                        Message = "A GPT partition has FirstLba greater than LastLba.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                    continue;
                }

                if (partition.FirstLba < table.FirstUsableLba || partition.LastLba > table.LastUsableLba)
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "GPT_LBA_BOUNDS",
                        Message = "A GPT partition falls outside usable LBA range.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                }

                intervals.Add((partition.FirstLba, partition.LastLba));
            }

            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = 1; i < intervals.Count; i++)
            {
                var previous = intervals[i - 1];
                var current = intervals[i];
                if (IntervalsOverlap(previous.Start, previous.End, current.Start, current.End))
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "GPT_OVERLAP",
                        Message = "GPT partitions contain overlapping LBA ranges.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                    return;
                }
            }
        }

        private static void AnalyzeMbr(MbrPartitionTable table, PartitionDiagnosticsReport report)
        {
            MbrPartitionEntry[] partitions = table.Partitions;
            bool hasProtective = false;
            bool hasRegular = false;

            for (int i = 0; i < partitions.Length; i++)
            {
                MbrPartitionEntry entry = partitions[i];
                if (entry.Status != 0x00 && entry.Status != 0x80)
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "MBR_STATUS",
                        Message = "MBR partition status must be 0x00 or 0x80.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                }

                if (entry.PartitionType == 0xEE)
                {
                    hasProtective = true;
                }
                else if (!entry.IsEmpty)
                {
                    hasRegular = true;
                }
            }

            if (hasProtective && hasRegular)
            {
                report.Issues.Add(new PartitionDiagnosticIssue
                {
                    Code = "MBR_HYBRID",
                    Message = "Hybrid MBR detected (protective and regular partitions mixed).",
                    Severity = PartitionIssueSeverity.Warning,
                    CanAutoRepair = false
                });
            }

            var intervals = new List<(ulong Start, ulong End)>();
            for (int i = 0; i < partitions.Length; i++)
            {
                MbrPartitionEntry entry = partitions[i];
                if (entry.IsEmpty || entry.SectorCount == 0)
                {
                    continue;
                }

                ulong end;
                try
                {
                    end = checked((ulong)entry.FirstLba + entry.SectorCount - 1);
                }
                catch (OverflowException)
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "MBR_LBA_OVERFLOW",
                        Message = "An MBR partition has an overflowing LBA range.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                    continue;
                }

                intervals.Add((entry.FirstLba, end));
            }

            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = 1; i < intervals.Count; i++)
            {
                var previous = intervals[i - 1];
                var current = intervals[i];
                if (IntervalsOverlap(previous.Start, previous.End, current.Start, current.End))
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "MBR_OVERLAP",
                        Message = "MBR partitions contain overlapping LBA ranges.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                    return;
                }
            }
        }

        private static void AnalyzeAmlogicEpt(AmlogicPartitionTable table, PartitionDiagnosticsReport report)
        {
            if (table.Partitions.Count == 0)
            {
                report.Issues.Add(new PartitionDiagnosticIssue
                {
                    Code = "AMLOGIC_EPT_EMPTY",
                    Message = "Amlogic EPT must contain at least one partition.",
                    Severity = PartitionIssueSeverity.Error,
                    CanAutoRepair = false
                });
            }

            if (!table.IsChecksumValid)
            {
                report.Issues.Add(new PartitionDiagnosticIssue
                {
                    Code = "AMLOGIC_EPT_CHECKSUM",
                    Message = "Amlogic EPT checksum mismatch.",
                    Severity = PartitionIssueSeverity.Error,
                    CanAutoRepair = true
                });
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var intervals = new List<(ulong Start, ulong End)>();
            for (int i = 0; i < table.Partitions.Count; i++)
            {
                AmlogicPartitionEntry partition = table.Partitions[i];
                if (string.IsNullOrWhiteSpace(partition.Name))
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "AMLOGIC_EPT_NAME_EMPTY",
                        Message = "An Amlogic EPT partition has an empty name.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                    continue;
                }

                if (!AmlogicPartitionTableSupport.IsValidPartitionName(partition.Name))
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "AMLOGIC_EPT_NAME_INVALID",
                        Message = "An Amlogic EPT partition name contains invalid characters or is longer than 15 ASCII bytes.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                }

                if (!names.Add(partition.Name))
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "AMLOGIC_EPT_NAME_DUP",
                        Message = "Amlogic EPT partitions contain duplicated names.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                }

                if (partition.Size == 0)
                {
                    continue;
                }

                ulong end;
                try
                {
                    end = checked(partition.Offset + partition.Size - 1);
                }
                catch (OverflowException)
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "AMLOGIC_EPT_RANGE_OVERFLOW",
                        Message = "An Amlogic EPT partition has an overflowing byte range.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                    continue;
                }

                intervals.Add((partition.Offset, end));
            }

            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = 1; i < intervals.Count; i++)
            {
                var previous = intervals[i - 1];
                var current = intervals[i];
                if (IntervalsOverlap(previous.Start, previous.End, current.Start, current.End))
                {
                    report.Issues.Add(new PartitionDiagnosticIssue
                    {
                        Code = "AMLOGIC_EPT_OVERLAP",
                        Message = "Amlogic EPT partitions contain overlapping byte ranges.",
                        Severity = PartitionIssueSeverity.Error,
                        CanAutoRepair = false
                    });
                    return;
                }
            }
        }

        private static bool IntervalsOverlap(ulong firstStart, ulong firstEnd, ulong secondStart, ulong secondEnd)
        {
            return firstStart <= secondEnd && secondStart <= firstEnd;
        }
    }
}
