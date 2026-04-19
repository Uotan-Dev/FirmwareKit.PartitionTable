namespace FirmwareKit.PartitionTable.Models
{
    internal static class AmlogicPartitionTableSupport
    {
        internal const int PartitionSlotCount = 32;

        internal static bool IsValidPartitionName(string? name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 15)
            {
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool isDigit = c >= '0' && c <= '9';
                bool isUpper = c >= 'A' && c <= 'Z';
                bool isLower = c >= 'a' && c <= 'z';
                if (!isDigit && !isUpper && !isLower && c != '-' && c != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}