using System;
using System.Collections;
using System.IO;

namespace MerlinAudit
{
    /// <summary>Tracks files that still need uploading after a crash or failed HTTP.</summary>
    internal static class AuditPendingQueue
    {
        private const string QueueFileName = "pending-upload-queue.txt";

        public static string QueuePath
        {
            get { return Path.Combine(AuditConfig.ConfigDirectory, QueueFileName); }
        }

        public static void MarkError()
        {
            Mark("error", AuditLocalErrorStore.FilePath);
        }

        public static void MarkRssiTrace()
        {
            Mark("rssi_trace", RssiTraceRecorder.PendingFilePath);
        }

        public static void MarkLabSnapshot()
        {
            Mark("lab", AuditLocalLabStore.FilePath);
        }

        private static void Mark(string kind, string payloadPath)
        {
            try
            {
                var lines = new ArrayList();
                if (File.Exists(QueuePath))
                {
                    string[] existing = CfCompat.ReadAllLines(QueuePath);
                    for (int i = 0; i < existing.Length; i++)
                    {
                        if (existing[i] != null && existing[i].Length > 0)
                        {
                            lines.Add(existing[i]);
                        }
                    }
                }
                string entry = kind + "|" + (payloadPath ?? "");
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (String.Compare((string)lines[i], entry, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) lines.Add(entry);
                CfCompat.WriteAllText(QueuePath, JoinLines(lines));
            }
            catch { }
        }

        public static void ClearEntry(string kind)
        {
            try
            {
                if (!File.Exists(QueuePath)) return;
                string[] existing = CfCompat.ReadAllLines(QueuePath);
                var keep = new ArrayList();
                string prefix = kind + "|";
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i] == null || existing[i].Length == 0) continue;
                    if (!existing[i].StartsWith(prefix)) keep.Add(existing[i]);
                }
                CfCompat.WriteAllText(QueuePath, JoinLines(keep));
            }
            catch { }
        }

        public static string[] ListKinds()
        {
            try
            {
                if (!File.Exists(QueuePath)) return new string[0];
                string[] existing = CfCompat.ReadAllLines(QueuePath);
                var kinds = new ArrayList();
                for (int i = 0; i < existing.Length; i++)
                {
                    string line = existing[i];
                    if (line == null || line.Length == 0) continue;
                    int sep = line.IndexOf('|');
                    if (sep > 0) kinds.Add(line.Substring(0, sep));
                }
                return (string[])kinds.ToArray(typeof(string));
            }
            catch
            {
                return new string[0];
            }
        }

        public static bool HasAny
        {
            get { return ListKinds().Length > 0 || AuditLocalErrorStore.HasPending; }
        }

        private static string JoinLines(ArrayList lines)
        {
            if (lines.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append("\r\n");
                sb.Append((string)lines[i]);
            }
            return sb.ToString();
        }
    }
}
