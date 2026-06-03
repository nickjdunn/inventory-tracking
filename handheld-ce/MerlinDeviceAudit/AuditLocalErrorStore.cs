using System;
using System.IO;
using System.Text;

namespace MerlinAudit
{
    /// <summary>
    /// Writes errors to disk first so a crash or failed upload can be retried on next launch.
    /// </summary>
    internal static class AuditLocalErrorStore
    {
        private const string FileName = "pending-audit-error.txt";
        private const string Sep = "---";

        public static string FilePath
        {
            get { return Path.Combine(AuditConfig.ConfigDirectory, FileName); }
        }

        public static bool HasPending
        {
            get { return File.Exists(FilePath); }
        }

        public static void Save(string context, string message, string detail)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("context=");
                sb.Append(context ?? "");
                sb.Append("\r\nmessage=");
                sb.Append(message ?? "");
                sb.Append("\r\napp=");
                sb.Append(AuditConfig.AppVersion);
                sb.Append("\r\ncaptured_at=");
                sb.Append(DateTime.UtcNow.Ticks / 10000L);
                sb.Append("\r\n");
                sb.Append(Sep);
                sb.Append("\r\n");
                sb.Append(detail ?? "");
                CfCompat.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }

        public static string PeekSummary()
        {
            try
            {
                if (!File.Exists(FilePath)) return "";
                string text = CfCompat.ReadAllText(FilePath);
                if (text == null) return "";
                if (text.Length > 240) text = text.Substring(0, 239) + "~";
                return text.Replace("\r", " ").Replace("\n", " ");
            }
            catch
            {
                return "";
            }
        }

        public static bool TryLoad(out string context, out string message, out string detail)
        {
            context = "";
            message = "";
            detail = "";
            try
            {
                if (!File.Exists(FilePath)) return false;
                string text = CfCompat.ReadAllText(FilePath);
                if (text == null || text.Length == 0) return false;

                int sep = text.IndexOf(Sep);
                string head = sep >= 0 ? text.Substring(0, sep) : text;
                detail = sep >= 0 && sep + Sep.Length < text.Length
                    ? text.Substring(sep + Sep.Length).Trim()
                    : "";

                string[] lines = head.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.StartsWith("context="))
                    {
                        context = line.Substring("context=".Length);
                    }
                    else if (line.StartsWith("message="))
                    {
                        message = line.Substring("message=".Length);
                    }
                }
                return context.Length > 0 || message.Length > 0 || detail.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch { }
        }
    }
}
