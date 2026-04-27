using FirmwareKit.PartitionTable.Models;
using System;
using System.Runtime.Serialization;

namespace FirmwareKit.PartitionTable.Exceptions
{
    /// <summary>
    /// Thrown when a partition-level operation fails (e.g. add, remove, update on a read-only table).
    /// 当分区级操作失败时抛出（如在只读表上添加、删除、更新）。
    /// </summary>
    [Serializable]
    public sealed class PartitionOperationException : PartitionTableException
    {
        /// <summary>
        /// Gets the zero-based partition index associated with the error, if applicable.
        /// 获取与错误关联的零基分区索引（如适用）。
        /// </summary>
        public int? PartitionIndex { get; }

        /// <summary>
        /// Initializes a new instance with a message, error code, optional partition index, and optional table type.
        /// 使用消息、错误代码、可选的分区索引和可选的表类型初始化新实例。
        /// </summary>
        public PartitionOperationException(string message, string errorCode, int? partitionIndex = null, PartitionTableType? tableType = null)
            : base(message, errorCode, tableType)
        {
            PartitionIndex = partitionIndex;
        }

        /// <summary>
        /// Initializes a new instance with a message, error code, partition index, table type, and inner exception.
        /// 使用消息、错误代码、分区索引、表类型和内部异常初始化新实例。
        /// </summary>
        public PartitionOperationException(string message, string errorCode, int? partitionIndex, PartitionTableType? tableType, Exception innerException)
            : base(message, errorCode, tableType, innerException)
        {
            PartitionIndex = partitionIndex;
        }

        private PartitionOperationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            int indexValue = info.GetInt32(nameof(PartitionIndex));
            PartitionIndex = indexValue >= 0 ? indexValue : (int?)null;
        }

        /// <summary>
        /// Sets the SerializationInfo with information about the exception.
        /// 使用异常信息设置序列化信息。
        /// </summary>
        /// <param name="info">The SerializationInfo that holds the serialized object data.</param>
        /// <param name="context">The StreamingContext that contains contextual information.</param>
#pragma warning disable CS0672 // Member overrides obsolete member
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
#pragma warning restore CS0672
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(PartitionIndex), PartitionIndex.HasValue ? PartitionIndex.Value : -1);
        }
    }
}
