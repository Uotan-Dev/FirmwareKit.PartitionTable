using FirmwareKit.PartitionTable.Models;
using System;
using System.Runtime.Serialization;

namespace FirmwareKit.PartitionTable.Exceptions
{
    /// <summary>
    /// Thrown when a partition table format cannot be recognized or is invalid.
    /// 当分区表格式无法识别或无效时抛出。
    /// </summary>
    [Serializable]
    public sealed class PartitionTableFormatException : PartitionTableException
    {
        /// <summary>
        /// Initializes a new instance with a message, error code, and optional table type.
        /// 使用消息、错误代码和可选的表类型初始化新实例。
        /// </summary>
        public PartitionTableFormatException(string message, string errorCode, PartitionTableType? tableType = null)
            : base(message, errorCode, tableType)
        {
        }

        /// <summary>
        /// Initializes a new instance with a message, error code, table type, and inner exception.
        /// 使用消息、错误代码、表类型和内部异常初始化新实例。
        /// </summary>
        public PartitionTableFormatException(string message, string errorCode, PartitionTableType? tableType, Exception innerException)
            : base(message, errorCode, tableType, innerException)
        {
        }

        private PartitionTableFormatException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
