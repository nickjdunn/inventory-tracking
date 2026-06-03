using System;
using System.Collections;

namespace MerlinAudit
{
    internal static class ScanInputClassifier
    {
        public static string Classify(string raw)
        {
            string trimmed = raw == null ? "" : raw.Trim();
            if (trimmed.Length == 0) return "empty";

            if (trimmed.IndexOf(',') >= 0) return "rfid";
            if (trimmed.IndexOf('|') >= 0)
            {
                string head = trimmed.Split('|')[0].Trim();
                if (IsMostlyHex(head) && head.Length >= 12) return "rfid";
            }
            if (IsNumericBarcode(trimmed)) return "barcode";

            if (IsMostlyHex(trimmed) && trimmed.Length >= 12) return "rfid";
            return "unknown";
        }

        public static string DelimiterStats(string raw)
        {
            if (raw == null || raw.Length == 0) return "len=0";
            int cr = 0;
            int lf = 0;
            int tab = 0;
            int pipe = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\r') cr++;
                else if (c == '\n') lf++;
                else if (c == '\t') tab++;
                else if (c == '|') pipe++;
            }
            return "len=" + raw.Length + " cr=" + cr + " lf=" + lf + " tab=" + tab + " pipe=" + pipe;
        }

        private static bool IsNumericBarcode(string s)
        {
            if (s.Length < 6 || s.Length > 20) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        private static bool IsMostlyHex(string s)
        {
            if (s == null || s.Length == 0) return false;
            int hex = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = char.ToUpper(s[i]);
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')) hex++;
            }
            return hex >= s.Length * 3 / 4;
        }
    }

    internal sealed class ScanCaptureEntry
    {
        public int StepIndex;
        public string StepLabel = "";
        public string ExpectedType = "";
        public string RawText = "";
        public string TrimmedText = "";
        public string ClassifiedType = "";
        public string Delimiters = "";
        public long TimestampUtc;
        public string Source = "wedge_focus";
        public bool MatchesExpected;

        public string ToJsonFragment()
        {
            return "{"
                + "\"step_index\":" + StepIndex
                + ",\"step_label\":\"" + SimpleJson.Escape(StepLabel) + "\""
                + ",\"expected_type\":\"" + SimpleJson.Escape(ExpectedType) + "\""
                + ",\"classified_type\":\"" + SimpleJson.Escape(ClassifiedType) + "\""
                + ",\"matches_expected\":" + (MatchesExpected ? "true" : "false")
                + ",\"raw_len\":" + (RawText != null ? RawText.Length : 0)
                + ",\"trimmed\":\"" + SimpleJson.Escape(TrimmedText) + "\""
                + ",\"delimiters\":\"" + SimpleJson.Escape(Delimiters) + "\""
                + ",\"timestamp_utc\":" + TimestampUtc
                + ",\"source\":\"" + SimpleJson.Escape(Source) + "\""
                + "}";
        }
    }

    internal static class ScanSessionJson
    {
        public static string BuildArrayJson(ArrayList entries)
        {
            if (entries == null || entries.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(((ScanCaptureEntry)entries[i]).ToJsonFragment());
            }
            return sb.ToString();
        }

        public static string BuildSessionObject(ArrayList entries, bool completed, int stepsTotal)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            AuditJson.AppendString(sb, "format", "merlin-scan-guide-v1");
            sb.Append(",");
            AuditJson.AppendBool(sb, "completed", completed);
            sb.Append(",");
            AuditJson.AppendLong(sb, "steps_total", stepsTotal);
            sb.Append(",");
            AuditJson.AppendLong(sb, "captures", entries != null ? entries.Count : 0);
            sb.Append(",");
            AuditJson.AppendArray(sb, "events", BuildArrayJson(entries));
            sb.Append("}");
            return sb.ToString();
        }
    }
}
