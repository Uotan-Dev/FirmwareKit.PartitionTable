using FirmwareKit.PartitionTable.Models;
using System;
using System.Runtime.Serialization;

namespace FirmwareKit.PartitionTable.Exceptions
{
    /// <summary>
    /// The base exception for all partition table operations.
    /// 所有分区表操作的基异常。
    /// </summary>
    [Serializable]
    public class PartitionTableException : Exception
    {
        /// <summary>
        /// Gets the machine-readable error code.
        /// 获取机器可读的错误代码。
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Gets the partition table type associated with the error, if known.
        /// 获取与错误关联的分区表类型（如已知）。
        /// </summary>
        public PartitionTableType? TableType { get; }

        /// <summary>
        /// Initializes a new instance with a message, error code, and optional table type.
        /// 使用消息、错误代码和可选的表类型初始化新实例。
        /// </summary>
        public PartitionTableException(string message, string errorCode, PartitionTableType? tableType = null)
            : base(message)
        {
            ErrorCode = errorCode ?? string.Empty;
            TableType = tableType;
        }

        /// <summary>
        /// Initializes a new instance with a message, error code, table type, and inner exception.
        /// 使用消息、错误代码、表类型和内部异常初始化新实例。
        /// </summary>
        public PartitionTableException(string message, string errorCode, PartitionTableType? tableType, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode ?? string.Empty;
            TableType = tableType;
        }

        /// <summary>
        /// Initializes a new instance with serialized data.
        /// 使用序列化数据初始化新实例。
        /// </summary>
        protected PartitionTableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ErrorCode = info.GetString(nameof(ErrorCode)) ?? string.Empty;
            int tableTypeValue = info.GetInt32(nameof(TableType));
            TableType = tableTypeValue >= 0 ? (PartitionTableType?)tableTypeValue : null;
        }

        /// <summary>
        /// Sets the SerializationInfo with information about the exception.
        /// 使用异常信息设置序列化信息。
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data. 包含序列化对象数据的 SerializationInfo。</param>
        /// <param name="context">The StreamingContext that contains contextual information about the source or destination. 包含有关源或目标上下文信息的 StreamingContext。</param>
#pragma warning disable CS0672 // Member overrides obsolete member
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
#pragma warning restore CS0672
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(ErrorCode), ErrorCode);
            info.AddValue(nameof(TableType), TableType.HasValue ? (int)TableType.Value : -1);
        }
    }
}
