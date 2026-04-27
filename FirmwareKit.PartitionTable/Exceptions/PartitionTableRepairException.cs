using FirmwareKit.PartitionTable.Models;
using System;
using System.Runtime.Serialization;

namespace FirmwareKit.PartitionTable.Exceptions
{
    /// <summary>
    /// Thrown when a partition table repair operation fails.
    /// 当分区表修复操作失败时抛出。
    /// </summary>
    [Serializable]
    public sealed class PartitionTableRepairException : PartitionTableException
    {
        /// <summary>
        /// Initializes a new instance with a message, error code, and optional table type.
        /// 使用消息、错误代码和可选的表类型初始化新实例。
        /// </summary>
        public PartitionTableRepairException(string message, string errorCode, PartitionTableType? tableType = null)
            : base(message, errorCode, tableType)
        {
        }

        /// <summary>
        /// Initializes a new instance with a message, error code, table type, and inner exception.
        /// 使用消息、错误代码、表类型和内部异常初始化新实例。
        /// </summary>
        public PartitionTableRepairException(string message, string errorCode, PartitionTableType? tableType, Exception innerException)
            : base(message, errorCode, tableType, innerException)
        {
        }

        private PartitionTableRepairException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
