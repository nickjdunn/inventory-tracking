using System;
using System.IO;
using System.Text;

namespace MerlinHandheld
{
    /// <summary>Append-only debug log next to the exe; upload via diagnostics screen.</summary>
    public static class DiagnosticLog
    {
        private static readonly object _lock = new object();
        private const int MaxFileBytes = 256000;

        public static string LogPath
        {
            get { return Path.Combine(AppConfig.ConfigDirectory, "merlin-debug.log"); }
        }

        public static void Write(string level, string message)
        {
            if (message == null) message = "";
            string line = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + "\r\n";
            lock (_lock)
            {
                try
                {
                    TrimIfNeeded();
                    CfCompat.AppendAllText(LogPath, line);
                }
                catch { }
            }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERR", message); }

        public static string ReadAll()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(LogPath)) return "";
                    return CfCompat.ReadAllText(LogPath);
                }
                catch
                {
                    return "";
                }
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(LogPath)) File.Delete(LogPath);
                }
                catch { }
            }
        }

        private static void TrimIfNeeded()
        {
            try
            {
                if (!File.Exists(LogPath)) return;
                var fi = new FileInfo(LogPath);
                if (fi.Length <= MaxFileBytes) return;
                string text = CfCompat.ReadAllText(LogPath);
                if (text.Length > MaxFileBytes / 2)
                {
                    text = text.Substring(text.Length - MaxFileBytes / 2);
                }
                CfCompat.WriteAllText(LogPath, text);
            }
            catch { }
        }
    }
}
