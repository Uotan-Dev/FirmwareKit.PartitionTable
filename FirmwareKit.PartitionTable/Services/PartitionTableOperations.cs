using FirmwareKit.PartitionTable.Interfaces;
using FirmwareKit.PartitionTable.Models;
using System;
using System.Collections.Generic;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// High-level partition operations and dry-run planning helpers.
    /// 高层分区操作与干跑规划辅助。
    /// </summary>
    public static class PartitionTableOperations
    {
        /// <summary>
        /// Aligns an LBA value up to the specified alignment.
        /// 将 LBA 向上对齐到指定对齐粒度。
        /// </summary>
        /// <param name="lba">The original LBA. / 原始 LBA。</param>
        /// <param name="alignment">Alignment in LBAs, must be greater than zero. / 对齐粒度（LBA），必须大于 0。</param>
        /// <returns>The aligned LBA. / 对齐后的 LBA。</returns>
        public static ulong AlignLba(ulong lba, ulong alignment)
        {
            if (alignment == 0) throw new ArgumentOutOfRangeException(nameof(alignment));
            ulong remainder = lba % alignment;
            return remainder == 0 ? lba : lba + (alignment - remainder);
        }

        /// <summary>
        /// Plans a new GPT partition in the first aligned free range that can fit the requested size.
        /// 在首个满足容量要求且对齐的空闲区间中规划新的 GPT 分区。
        /// </summary>
        /// <param name="table">The source GPT table. / 源 GPT 表。</param>
        /// <param name="sectorCount">Requested partition length in sectors. / 目标分区长度（扇区数）。</param>
        /// <param name="alignmentLba">Alignment in LBAs, must be greater than zero. / 对齐粒度（LBA），必须大于 0。</param>
        /// <param name="name">Partition name. / 分区名称。</param>
        /// <param name="partitionType">GPT partition type GUID. / GPT 分区类型 GUID。</param>
        /// <param name="partitionId">GPT partition unique GUID. / GPT 分区唯一 GUID。</param>
        /// <returns>A planned GPT partition entry. / 规划得到的 GPT 分区项。</returns>
        public static GptPartitionEntry PlanAlignedGptPartition(GptPartitionTable table, ulong sectorCount, ulong alignmentLba, string name, Guid partitionType, Guid partitionId)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (sectorCount == 0) throw new ArgumentOutOfRangeException(nameof(sectorCount));
            if (alignmentLba == 0) throw new ArgumentOutOfRangeException(nameof(alignmentLba));

            var intervals = new List<(ulong Start, ulong End)>();
            for (int i = 0; i < table.Partitions.Count; i++)
            {
                GptPartitionEntry partition = table.Partitions[i];
                if (partition.FirstLba <= partition.LastLba)
                {
                    ulong start = partition.FirstLba < table.FirstUsableLba ? table.FirstUsableLba : partition.FirstLba;
                    ulong end = partition.LastLba > table.LastUsableLba ? table.LastUsableLba : partition.LastLba;
                    if (start <= end)
                    {
                        intervals.Add((start, end));
                    }
                }
            }

            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));

            ulong cursor = AlignLba(table.FirstUsableLba, alignmentLba);
            for (int i = 0; i <= intervals.Count; i++)
            {
                ulong rangeEnd;
                if (i == intervals.Count)
                {
                    rangeEnd = table.LastUsableLba;
                }
                else
                {
                    ulong intervalStart = intervals[i].Start;
                    rangeEnd = intervalStart == 0 ? 0 : intervalStart - 1;
                }

                if (rangeEnd >= cursor)
                {
                    ulong available = rangeEnd - cursor + 1;
                    if (available >= sectorCount)
                    {
                        ulong plannedEnd = checked(cursor + sectorCount - 1);
                        return new GptPartitionEntry
                        {
                            PartitionType = partitionType,
                            PartitionId = partitionId,
                            FirstLba = cursor,
                            LastLba = plannedEnd,
                            Attributes = 0,
                            Name = name ?? string.Empty
                        };
                    }
                }

                if (i < intervals.Count)
                {
                    if (intervals[i].End == ulong.MaxValue)
                    {
                        break;
                    }

                    ulong nextCursor = AlignLba(intervals[i].End + 1, alignmentLba);
                    if (nextCursor > cursor)
                    {
                        cursor = nextCursor;
                    }
                }
            }

            throw new InvalidOperationException("No aligned free GPT range can satisfy the requested sector count.");
        }

        /// <summary>
        /// Builds a dry-run write plan describing ranges that will be written.
        /// 构建干跑写入计划，描述将被写入的范围。
        /// </summary>
        /// <param name="table">The partition table to evaluate. / 待评估的分区表。</param>
        /// <returns>The write plan. / 写入计划。</returns>
        public static PartitionWritePlan BuildWritePlan(IPartitionTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var plan = new PartitionWritePlan { Type = table.Type };
            if (table is MbrPartitionTable)
            {
                plan.Ranges.Add(new PartitionWriteRange
                {
                    Offset = 0,
                    Length = 512,
                    Description = "MBR sector"
                });

                return plan;
            }

            if (table is GptPartitionTable gpt)
            {
                int entryTableLength = checked((int)(gpt.PartitionsCount * gpt.PartitionEntrySize));
                ulong tableSectors = ((ulong)entryTableLength + (ulong)gpt.SectorSize - 1) / (ulong)gpt.SectorSize;
                ulong backupEntriesLba = gpt.BackupLba - tableSectors;

                plan.Ranges.Add(new PartitionWriteRange { Offset = 0, Length = 512, Description = "Protective MBR" });
                plan.Ranges.Add(new PartitionWriteRange { Offset = CheckedByteOffset(gpt.CurrentLba, gpt.SectorSize), Length = gpt.SectorSize, Description = "GPT primary header" });
                plan.Ranges.Add(new PartitionWriteRange { Offset = CheckedByteOffset(gpt.PartitionEntriesLba, gpt.SectorSize), Length = entryTableLength, Description = "GPT primary entry array" });
                plan.Ranges.Add(new PartitionWriteRange { Offset = CheckedByteOffset(backupEntriesLba, gpt.SectorSize), Length = entryTableLength, Description = "GPT backup entry array" });
                plan.Ranges.Add(new PartitionWriteRange { Offset = CheckedByteOffset(gpt.BackupLba, gpt.SectorSize), Length = gpt.SectorSize, Description = "GPT backup header" });
                return plan;
            }

            if (table is AmlogicPartitionTable)
            {
                plan.Ranges.Add(new PartitionWriteRange
                {
                    Offset = 0,
                    Length = 24 + (32 * 40),
                    Description = "Amlogic EPT table"
                });
            }

            return plan;
        }

        private static long CheckedByteOffset(ulong lba, int sectorSize)
        {
            ulong byteOffset = checked(lba * (ulong)sectorSize);
            if (byteOffset > long.MaxValue)
            {
                throw new OverflowException("Computed byte offset exceeds Int64 range.");
            }

            return (long)byteOffset;
        }
    }
}
