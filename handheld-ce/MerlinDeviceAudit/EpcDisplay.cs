namespace MerlinAudit
{
    /// <summary>Compact EPC labels for small CE screens (suffix = unique part on many tags).</summary>
    internal static class EpcDisplay
    {
        public const int ListSuffixLen = 6;
        public const int TargetSuffixLen = 10;

        public static string Suffix(string epc, int tailLen)
        {
            if (epc == null) return "";
            epc = epc.Trim().ToUpper();
            if (epc.Length <= tailLen) return epc;
            return epc.Substring(epc.Length - tailLen);
        }

        /// <summary>e.g. -62 ·86799 (last 6 hex chars).</summary>
        public static string ListLine(int rssi, bool hasRssi, string epc)
        {
            string tail = Suffix(epc, ListSuffixLen);
            if (hasRssi) return rssi + " ·" + tail;
            return "-- ·" + tail;
        }

        public static string TargetLabel(string epc)
        {
            if (epc == null || epc.Length == 0) return "(none)";
            if (epc.Length <= TargetSuffixLen + 4) return epc;
            return "…" + Suffix(epc, TargetSuffixLen);
        }
    }
}
