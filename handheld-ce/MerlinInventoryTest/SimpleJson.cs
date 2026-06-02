using System;
using System.Text.RegularExpressions;

namespace MerlinHandheld
{
    /// <summary>Minimal JSON helpers for .NET CF (no external JSON library).</summary>
    public static class SimpleJson
    {
        public static string Escape(string value)
        {
            if (value == null) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        public static string ExtractString(string json, string key)
        {
            if (json == null || key == null) return "";
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"";
            Match m = Regex.Match(json, pattern);
            return m.Success ? m.Groups[1].Value : "";
        }

        public static string ExtractNestedString(string json, string objectKey, string fieldKey)
        {
            if (json == null) return "";
            int objIdx = json.IndexOf("\"" + objectKey + "\"");
            if (objIdx < 0) return "";
            return ExtractString(json.Substring(objIdx), fieldKey);
        }

        public static int ExtractInt(string json, string key, int fallback)
        {
            if (json == null || key == null) return fallback;
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)";
            Match m = Regex.Match(json, pattern);
            if (!m.Success) return fallback;
            int n;
            return CfCompat.TryParseInt(m.Groups[1].Value, out n) ? n : fallback;
        }

        public static int ExtractNestedInt(string json, string objectKey, string fieldKey, int fallback)
        {
            if (json == null) return fallback;
            int objIdx = json.IndexOf("\"" + objectKey + "\"");
            if (objIdx < 0) return fallback;
            return ExtractInt(json.Substring(objIdx), fieldKey, fallback);
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
