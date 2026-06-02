using System;
using System.Collections;
using System.Text;

namespace MerlinStream
{
    /// <summary>Parse helpers — no tag-count cap (wedge delimiter fixed on device).</summary>
    internal static class ScanLimits
    {
        public const int MaxTagsInSummary = 12;
        public const int MaxWedgeChars = 64000;
        public const int MaxWedgeBufferChars = 48000;

        public static string TrimWedge(string raw)
        {
            if (raw == null) return "";
            if (raw.Length <= MaxWedgeChars) return raw;
            return raw.Substring(0, MaxWedgeChars);
        }

        public static void DedupeByEpc(ArrayList tags)
        {
            if (tags == null || tags.Count < 2) return;
            var seen = new Hashtable();
            for (int i = tags.Count - 1; i >= 0; i--)
            {
                var t = (TagRead)tags[i];
                string key = t.Epc == null ? "" : t.Epc.ToLower();
                if (key.Length == 0)
                {
                    tags.RemoveAt(i);
                    continue;
                }
                if (seen.Contains(key))
                {
                    tags.RemoveAt(i);
                }
                else
                {
                    seen[key] = true;
                }
            }
        }

        public static ArrayList ParseTags(string raw)
        {
            ArrayList tags = TagParser.ParseText(TrimWedge(raw));
            DedupeByEpc(tags);
            return tags;
        }

        public static string FormatSummary(ArrayList tags)
        {
            if (tags == null || tags.Count == 0) return "";
            int show = tags.Count < MaxTagsInSummary ? tags.Count : MaxTagsInSummary;
            var sb = new StringBuilder();
            for (int i = 0; i < show; i++)
            {
                if (i > 0) sb.Append("\r\n");
                var t = (TagRead)tags[i];
                sb.Append(t.Epc);
                if (t.Rssi.HasValue) sb.Append("|").Append(t.Rssi.Value);
            }
            int extra = tags.Count - show;
            if (extra > 0)
            {
                sb.Append("\r\n… +").Append(extra).Append(" more");
            }
            return sb.ToString();
        }
    }
}
