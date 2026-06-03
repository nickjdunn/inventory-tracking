using System;
using System.Text.RegularExpressions;

namespace MerlinAudit
{
    public static class SimpleJson
    {
        public static string Escape(string value)
        {
            if (value == null) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        public static string ExtractString(string json, string key)
        {
            if (json == null || key == null) return "";
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"";
            Match m = Regex.Match(json, pattern);
            return m.Success ? m.Groups[1].Value : "";
        }

        public static bool ExtractBool(string json, string key, bool fallback)
        {
            if (json == null || key == null) return fallback;
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)";
            Match m = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) return fallback;
            return string.Compare(m.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
