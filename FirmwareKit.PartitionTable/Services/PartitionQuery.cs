using FirmwareKit.PartitionTable.Models;
using System;
using System.Collections.Generic;

namespace FirmwareKit.PartitionTable.Services
{
    /// <summary>
    /// Provides query operations for partition tables, including finding partitions by name or type
    /// and computing free space ranges.
    /// 提供分区表的查询操作，包括按名称或类型查找分区以及计算空闲空间范围。
    /// </summary>
    public static class PartitionQuery
    {
        /// <summary>
        /// Finds a GPT partition by name.
        /// 按名称查找 GPT 分区。
        /// </summary>
        /// <param name="table">The GPT partition table to search.</param>
        /// <param name="name">The partition name to find.</param>
        /// <param name="comparison">The string comparison mode.</param>
        /// <returns>The matching partition, or <see langword="null" /> if not found.</returns>
        public static GptPartitionEntry? FindGptPartitionByName(GptPartitionTable table, string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (name == null) throw new ArgumentNullException(nameof(name));

            for (int i = 0; i < table.Partitions.Count; i++)
            {
                if (string.Equals(table.Partitions[i].Name, name, comparison))
                {
                    return table.Partitions[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the index of a GPT partition by name.
        /// 按名称查找 GPT 分区的索引。
        /// </summary>
        /// <param name="table">The GPT partition table to search.</param>
        /// <param name="name">The partition name to find.</param>
        /// <param name="comparison">The string comparison mode.</param>
        /// <returns>The zero-based index, or -1 if not found.</returns>
        public static int IndexOfGptPartitionByName(GptPartitionTable table, string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (name == null) throw new ArgumentNullException(nameof(name));

            for (int i = 0; i < table.Partitions.Count; i++)
            {
                if (string.Equals(table.Partitions[i].Name, name, comparison))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Finds all GPT partitions matching a specific partition type GUID.
        /// 查找所有匹配特定分区类型 GUID 的 GPT 分区。
        /// </summary>
        /// <param name="table">The GPT partition table to search.</param>
        /// <param name="partitionType">The partition type GUID to match.</param>
        /// <returns>A list of matching partitions.</returns>
        public static List<GptPartitionEntry> FindGptPartitionsByType(GptPartitionTable table, Guid partitionType)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var results = new List<GptPartitionEntry>();
            for (int i = 0; i < table.Partitions.Count; i++)
            {
                if (table.Partitions[i].PartitionType == partitionType)
                {
                    results.Add(table.Partitions[i]);
                }
            }

            return results;
        }

        /// <summary>
        /// Finds an Amlogic EPT partition by name.
        /// 按名称查找 Amlogic EPT 分区。
        /// </summary>
        /// <param name="table">The Amlogic EPT partition table to search.</param>
        /// <param name="name">The partition name to find.</param>
        /// <param name="comparison">The string comparison mode.</param>
        /// <returns>The matching partition, or <see langword="null" /> if not found.</returns>
        public static AmlogicPartitionEntry? FindAmlogicPartitionByName(AmlogicPartitionTable table, string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (name == null) throw new ArgumentNullException(nameof(name));

            for (int i = 0; i < table.Partitions.Count; i++)
            {
                if (string.Equals(table.Partitions[i].Name, name, comparison))
                {
                    return table.Partitions[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the index of an Amlogic EPT partition by name.
        /// 按名称查找 Amlogic EPT 分区的索引。
        /// </summary>
        /// <param name="table">The Amlogic EPT partition table to search.</param>
        /// <param name="name">The partition name to find.</param>
        /// <param name="comparison">The string comparison mode.</param>
        /// <returns>The zero-based index, or -1 if not found.</returns>
        public static int IndexOfAmlogicPartitionByName(AmlogicPartitionTable table, string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (name == null) throw new ArgumentNullException(nameof(name));

            for (int i = 0; i < table.Partitions.Count; i++)
            {
                if (string.Equals(table.Partitions[i].Name, name, comparison))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Computes the free (unallocated) LBA ranges in a GPT partition table.
        /// 计算 GPT 分区表中的空闲（未分配）LBA 范围。
        /// </summary>
        /// <param name="table">The GPT partition table.</param>
        /// <returns>A sorted list of free ranges.</returns>
        public static List<GptFreeRange> GetGptFreeRanges(GptPartitionTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var occupied = new List<(ulong Start, ulong End)>();
            for (int i = 0; i < table.Partitions.Count; i++)
            {
                GptPartitionEntry partition = table.Partitions[i];
                if (partition.IsEmpty || partition.FirstLba > partition.LastLba)
                {
                    continue;
                }

                ulong start = Math.Max(partition.FirstLba, table.FirstUsableLba);
                ulong end = Math.Min(partition.LastLba, table.LastUsableLba);
                if (start <= end)
                {
                    occupied.Add((start, end));
                }
            }

            occupied.Sort((a, b) => a.Start.CompareTo(b.Start));

            var freeRanges = new List<GptFreeRange>();
            ulong cursor = table.FirstUsableLba;

            for (int i = 0; i < occupied.Count; i++)
            {
                if (occupied[i].Start > cursor)
                {
                    freeRanges.Add(new GptFreeRange
                    {
                        FirstLba = cursor,
                        LastLba = occupied[i].Start - 1,
                        SectorCount = occupied[i].Start - cursor
                    });
                }

                if (occupied[i].End >= cursor)
                {
                    cursor = occupied[i].End + 1;
                }
            }

            if (cursor <= table.LastUsableLba)
            {
                freeRanges.Add(new GptFreeRange
                {
                    FirstLba = cursor,
                    LastLba = table.LastUsableLba,
                    SectorCount = table.LastUsableLba - cursor + 1
                });
            }

            return freeRanges;
        }

        /// <summary>
        /// Computes the free (unallocated) byte ranges in an Amlogic EPT partition table.
        /// 计算 Amlogic EPT 分区表中的空闲（未分配）字节范围。
        /// </summary>
        /// <param name="table">The Amlogic EPT partition table.</param>
        /// <param name="totalSize">The total size of the disk image in bytes.</param>
        /// <returns>A sorted list of free ranges.</returns>
        public static List<AmlogicFreeRange> GetAmlogicFreeRanges(AmlogicPartitionTable table, ulong totalSize)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var occupied = new List<(ulong Offset, ulong End)>();
            for (int i = 0; i < table.Partitions.Count; i++)
            {
                AmlogicPartitionEntry partition = table.Partitions[i];
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
                    continue;
                }

                occupied.Add((partition.Offset, end));
            }

            occupied.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            var freeRanges = new List<AmlogicFreeRange>();
            ulong cursor = 0;

            for (int i = 0; i < occupied.Count; i++)
            {
                if (occupied[i].Offset > cursor)
                {
                    freeRanges.Add(new AmlogicFreeRange
                    {
                        Offset = cursor,
                        Size = occupied[i].Offset - cursor
                    });
                }

                if (occupied[i].End >= cursor)
                {
                    cursor = occupied[i].End + 1;
                }
            }

            if (cursor < totalSize)
            {
                freeRanges.Add(new AmlogicFreeRange
                {
                    Offset = cursor,
                    Size = totalSize - cursor
                });
            }

            return freeRanges;
        }

        /// <summary>
        /// Finds all MBR partitions matching a specific partition type byte.
        /// 查找所有匹配特定分区类型字节的 MBR 分区。
        /// </summary>
        /// <param name="table">The MBR partition table to search.</param>
        /// <param name="partitionType">The partition type byte to match.</param>
        /// <returns>A list of matching partition entries with their indices.</returns>
        public static List<(int Index, MbrPartitionEntry Entry)> FindMbrPartitionsByType(MbrPartitionTable table, byte partitionType)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var results = new List<(int Index, MbrPartitionEntry Entry)>();
            MbrPartitionEntry[] partitions = table.Partitions;
            for (int i = 0; i < partitions.Length; i++)
            {
                if (partitions[i].PartitionType == partitionType)
                {
                    results.Add((i, partitions[i]));
                }
            }

            return results;
        }

        /// <summary>
        /// Finds the first MBR partition with a specific partition type byte.
        /// 查找第一个匹配特定分区类型字节的 MBR 分区。
        /// </summary>
        /// <param name="table">The MBR partition table to search.</param>
        /// <param name="partitionType">The partition type byte to match.</param>
        /// <returns>The matching partition and its index, or null if not found.</returns>
        public static (int Index, MbrPartitionEntry Entry)? FindFirstMbrPartitionByType(MbrPartitionTable table, byte partitionType)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            MbrPartitionEntry[] partitions = table.Partitions;
            for (int i = 0; i < partitions.Length; i++)
            {
                if (partitions[i].PartitionType == partitionType)
                {
                    return (i, partitions[i]);
                }
            }

            return null;
        }

        /// <summary>
        /// Computes the free (unallocated) sector ranges in an MBR partition table.
        /// 计算 MBR 分区表中的空闲（未分配）扇区范围。
        /// </summary>
        /// <param name="table">The MBR partition table.</param>
        /// <param name="totalSectors">The total number of sectors on the disk.</param>
        /// <returns>A sorted list of free ranges.</returns>
        public static List<MbrFreeRange> GetMbrFreeRanges(MbrPartitionTable table, uint totalSectors)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var occupied = new List<(uint Start, uint End)>();
            MbrPartitionEntry[] partitions = table.Partitions;
            for (int i = 0; i < partitions.Length; i++)
            {
                MbrPartitionEntry entry = partitions[i];
                if (entry.IsEmpty || entry.SectorCount == 0)
                {
                    continue;
                }

                uint end;
                try
                {
                    end = checked(entry.FirstLba + entry.SectorCount - 1);
                }
                catch (OverflowException)
                {
                    continue;
                }

                occupied.Add((entry.FirstLba, end));
            }

            occupied.Sort((a, b) => a.Start.CompareTo(b.Start));

            var freeRanges = new List<MbrFreeRange>();
            uint cursor = 1;

            for (int i = 0; i < occupied.Count; i++)
            {
                if (occupied[i].Start > cursor)
                {
                    freeRanges.Add(new MbrFreeRange
                    {
                        FirstLba = cursor,
                        SectorCount = occupied[i].Start - cursor
                    });
                }

                if (occupied[i].End >= cursor)
                {
                    cursor = occupied[i].End + 1;
                }
            }

            if (cursor < totalSectors)
            {
                freeRanges.Add(new MbrFreeRange
                {
                    FirstLba = cursor,
                    SectorCount = totalSectors - cursor
                });
            }

            return freeRanges;
        }
    }
}
