using System;
using System.Collections.Generic;

namespace FirmwareKit.PartitionTable
{
    /// <summary>
    /// Controls probing behavior when parsing partition tables.
    /// 控制解析分区表时的探测行为。
    /// </summary>
    public sealed class PartitionReadOptions
    {
        /// <summary>
        /// Gets or sets the preferred sector size used during GPT probing.
        /// 获取或设置 GPT 探测时优先使用的扇区大小。
        /// </summary>
        public int? PreferredSectorSize { get; set; }

        /// <summary>
        /// Gets or sets whether probing should fail when the preferred sector size does not match.
        /// 获取或设置当首选扇区大小不匹配时是否直接失败。
        /// </summary>
        public bool StrictSectorSize { get; set; }

        /// <summary>
        /// Gets or sets additional sector sizes to probe.
        /// 获取或设置额外要探测的扇区大小集合。
        /// </summary>
        public IReadOnlyList<int>? ProbeSectorSizes { get; set; }

        internal IReadOnlyList<int> GetProbeSectorSizes()
        {
            if (ProbeSectorSizes == null || ProbeSectorSizes.Count == 0)
            {
                return Array.Empty<int>();
            }

            return ProbeSectorSizes;
        }
    }
}

