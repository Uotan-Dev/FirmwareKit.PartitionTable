using FirmwareKit.PartitionTable.Models;
using System;
using System.Runtime.Serialization;

namespace FirmwareKit.PartitionTable.Exceptions
{
    /// <summary>
    /// Thrown when a partition table checksum or CRC validation fails.
    /// 当分区表校验和或 CRC 验证失败时抛出。
    /// </summary>
    [Serializable]
    public sealed class PartitionTableChecksumException : PartitionTableException
    {
        /// <summary>
        /// Gets the kind of checksum that failed (e.g. "HeaderCrc", "EntryCrc", "AmlogicChecksum").
        /// 获取失败的校验和类型（如 "HeaderCrc"、"EntryCrc"、"AmlogicChecksum"）。
        /// </summary>
        public string ChecksumKind { get; }

        /// <summary>
        /// Initializes a new instance with a message, error code, checksum kind, and optional table type.
        /// 使用消息、错误代码、校验和类型和可选的表类型初始化新实例。
        /// </summary>
        public PartitionTableChecksumException(string message, string errorCode, string checksumKind, PartitionTableType? tableType = null)
            : base(message, errorCode, tableType)
        {
            ChecksumKind = checksumKind ?? string.Empty;
        }

        /// <summary>
        /// Initializes a new instance with a message, error code, checksum kind, table type, and inner exception.
        /// 使用消息、错误代码、校验和类型、表类型和内部异常初始化新实例。
        /// </summary>
        public PartitionTableChecksumException(string message, string errorCode, string checksumKind, PartitionTableType? tableType, Exception innerException)
            : base(message, errorCode, tableType, innerException)
        {
            ChecksumKind = checksumKind ?? string.Empty;
        }

        private PartitionTableChecksumException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ChecksumKind = info.GetString(nameof(ChecksumKind)) ?? string.Empty;
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
            info.AddValue(nameof(ChecksumKind), ChecksumKind);
        }
    }
}
