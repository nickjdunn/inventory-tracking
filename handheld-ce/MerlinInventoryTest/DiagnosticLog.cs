using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;

namespace MerlinHandheld
{
    /// <summary>
    /// Scanner trace log: in-memory ring (always when enabled) + file on a writable folder.
    /// Upload via Diag → Upload log.
    /// </summary>
    public static class DiagnosticLog
    {
        private static readonly object _lock = new object();
        private const int MaxFileBytes = 256000;
        private const int MaxMemoryChars = 120000;
        private const int MaxRawLogChars = 4000;
        private const int MaxTagsListed = 48;

        private static bool _enabled;
        private static bool _streamToServer;
        private static readonly StringBuilder _memory = new StringBuilder();
        private static readonly StringBuilder _uploadPending = new StringBuilder();
        private static InventoryApiClient _uploadApi;
        private static bool _uploadWorkerRunning;
        private static string _filePath = "";
        private static string _lastWriteError = "";
        private static string _lastUploadError = "";
        private static int _lineCount;
        private const int MaxUploadChunkChars = 6000;

        public static bool IsEnabled
        {
            get { return _enabled; }
        }

        public static string ActiveFilePath
        {
            get { return _filePath ?? ""; }
        }

        public static string LastWriteError
        {
            get { return _lastWriteError ?? ""; }
        }

        public static int LineCount
        {
            get { return _lineCount; }
        }

        public static string LastUploadError
        {
            get { return _lastUploadError ?? ""; }
        }

        public static void SetUploadClient(InventoryApiClient api)
        {
            _uploadApi = api;
        }

        public static void Configure(bool enabled)
        {
            lock (_lock)
            {
                _enabled = enabled;
                _streamToServer = enabled;
                if (enabled && _filePath.Length == 0)
                {
                    _filePath = ResolveWritableLogPath();
                }
                if (!enabled)
                {
                    _uploadPending.Length = 0;
                }
            }
        }

        /// <summary>Recent lines for on-gun preview (newest at end).</summary>
        public static string GetRecentTail(int maxChars)
        {
            if (maxChars < 200) maxChars = 200;
            lock (_lock)
            {
                string text = _memory.ToString();
                if (text.Length == 0) text = ReadFileText();
                if (text.Length <= maxChars) return text;
                return "...[tail]\r\n" + text.Substring(text.Length - maxChars);
            }
        }

        public static void LogSessionStart(AppConfig cfg)
        {
            if (!_enabled) return;
            Write("INFO", "======== session start ========");
            Write("INFO", "app=" + AppConfig.AppVersion + " scanner=" + (cfg.ScannerId ?? ""));
            Write("INFO", "server=" + (cfg.ServerUrl ?? ""));
            Write("INFO", "live_raw=" + cfg.LiveRawStream + " live_scan=" + cfg.LiveScanStream);
            Write("INFO", "log_file=" + ActiveFilePath);
            Write("INFO", "config=" + AppConfig.ConfigPath);
        }

        public static void LogInbound(string source, string hardwareMode, string uiMode, string raw)
        {
            if (!_enabled || raw == null || raw.Length == 0) return;
            Write("SCAN", source + " hw=" + hardwareMode + " ui=" + (uiMode ?? "")
                + " len=" + raw.Length + " " + DelimiterStats(raw));
            Write("RAW", FormatRawDump(raw));
        }

        public static void LogParsedTags(string context, ArrayList tags, int rawLen)
        {
            if (!_enabled) return;
            int n = tags == null ? 0 : tags.Count;
            Write("PARSE", context + " raw_len=" + rawLen + " tags=" + n);
            if (tags == null || n == 0) return;
            int show = n < MaxTagsListed ? n : MaxTagsListed;
            for (int i = 0; i < show; i++)
            {
                var t = (TagRead)tags[i];
                string rssi = t.Rssi.HasValue ? (" rssi=" + t.Rssi.Value) : "";
                Write("TAG", (i + 1) + "/" + n + " " + (t.Epc ?? "") + rssi);
            }
            if (n > MaxTagsListed)
            {
                Write("TAG", "... +" + (n - MaxTagsListed) + " more not listed");
            }
        }

        public static void LogLivePost(
            string uiMode,
            int rawLen,
            int tagCount,
            bool applyScan,
            bool attempted,
            HttpResult res)
        {
            if (!_enabled) return;
            if (!attempted)
            {
                Write("LIVE", "skip ui=" + (uiMode ?? "") + " raw=" + rawLen + " tags=" + tagCount
                    + " (live_raw/live_scan off or debounced)");
                return;
            }
            string status = res == null ? "null" : (res.Ok ? "OK" : "FAIL");
            string err = (res != null && res.Error != null && res.Error.Length > 0)
                ? (" err=" + MerlinUi.ShortLine(res.Error, 120))
                : "";
            Write("LIVE", "POST " + status + " ui=" + (uiMode ?? "") + " raw=" + rawLen
                + " tags=" + tagCount + " apply_scan=" + applyScan + err);
        }

        public static void LogHttp(string label, HttpResult res)
        {
            if (!_enabled) return;
            if (res == null)
            {
                Write("HTTP", label + " null");
                return;
            }
            Write("HTTP", label + " " + (res.Ok ? "OK" : "FAIL")
                + (res.Error != null && res.Error.Length > 0
                    ? (" " + MerlinUi.ShortLine(res.Error, 100))
                    : ""));
        }

        public static void LogNur(string message)
        {
            if (!_enabled) return;
            Write("NUR", message);
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERR", message); }

        public static void Write(string level, string message)
        {
            if (!_enabled) return;
            if (message == null) message = "";
            string ts = FormatTimestamp();
            string line = ts + " [" + level + "] " + message + "\r\n";
            lock (_lock)
            {
                _lineCount++;
                AppendMemory(line);
                AppendFile(line);
                if (_streamToServer)
                {
                    _uploadPending.Append(line);
                }
            }
            ScheduleServerUpload();
        }

        public static void FlushToServerNow()
        {
            ScheduleServerUpload();
        }

        private static void ScheduleServerUpload()
        {
            if (!_streamToServer || _uploadApi == null) return;
            if (_uploadWorkerRunning) return;
            _uploadWorkerRunning = true;
            ThreadPool.QueueUserWorkItem(delegate { RunServerUploadWorker(); });
        }

        private static void RunServerUploadWorker()
        {
            try
            {
                Thread.Sleep(250);
                while (true)
                {
                    string chunk = TakeUploadChunk();
                    if (chunk == null || chunk.Length == 0) break;
                    HttpResult res = _uploadApi.UploadDiagnosticLog(chunk, false);
                    if (!res.Ok)
                    {
                        _lastUploadError = res.Error != null ? res.Error : "upload failed";
                        lock (_lock)
                        {
                            if (_uploadPending.Length < 50000)
                            {
                                _uploadPending.Insert(0, chunk);
                            }
                        }
                        break;
                    }
                    _lastUploadError = "";
                }
            }
            finally
            {
                _uploadWorkerRunning = false;
                lock (_lock)
                {
                    if (_streamToServer && _uploadPending.Length > 0 && _uploadApi != null)
                    {
                        ScheduleServerUpload();
                    }
                }
            }
        }

        private static string TakeUploadChunk()
        {
            lock (_lock)
            {
                if (_uploadPending.Length == 0) return "";
                int len = _uploadPending.Length;
                if (len > MaxUploadChunkChars) len = MaxUploadChunkChars;
                string all = _uploadPending.ToString();
                string chunk = all.Substring(0, len);
                _uploadPending.Remove(0, len);
                return chunk;
            }
        }

        public static string ReadAll()
        {
            return ExportForUpload();
        }

        /// <summary>File + memory + diagnostics header for server upload.</summary>
        public static string ExportForUpload()
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.Append("=== Merlin scanner trace export ===\r\n");
                sb.Append("exported=").Append(FormatTimestamp()).Append("\r\n");
                sb.Append("lines=").Append(_lineCount).Append("\r\n");
                sb.Append("enabled=").Append(_enabled).Append("\r\n");
                sb.Append("file=").Append(ActiveFilePath).Append("\r\n");
                if (_lastWriteError.Length > 0)
                {
                    sb.Append("file_error=").Append(_lastWriteError).Append("\r\n");
                }
                sb.Append("---\r\n");

                string fileText = ReadFileText();
                if (fileText.Length > 0)
                {
                    sb.Append(fileText);
                    if (!fileText.EndsWith("\n") && !fileText.EndsWith("\r"))
                    {
                        sb.Append("\r\n");
                    }
                }

                if (_memory.Length > 0)
                {
                    sb.Append("--- memory buffer ---\r\n");
                    sb.Append(_memory.ToString());
                }

                if (sb.Length < 80)
                {
                    sb.Append("(no log lines captured — enable Log to file, tap Save stream opts, scan, then Upload)\r\n");
                }
                return sb.ToString();
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _memory.Length = 0;
                _lineCount = 0;
                _lastWriteError = "";
                try
                {
                    if (_filePath.Length > 0 && File.Exists(_filePath))
                    {
                        File.Delete(_filePath);
                    }
                }
                catch (Exception ex)
                {
                    _lastWriteError = ex.Message;
                }
            }
        }

        private static string FormatTimestamp()
        {
            try
            {
                DateTime now = DateTime.Now;
                return now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "time?";
            }
        }

        private static string FormatRawDump(string raw)
        {
            if (raw == null) return "(null)";
            string s = raw;
            if (s.Length > MaxRawLogChars)
            {
                s = s.Substring(0, MaxRawLogChars);
                return s + " ...[truncated, total " + raw.Length + " chars]";
            }
            return s;
        }

        private static string DelimiterStats(string raw)
        {
            if (raw == null || raw.Length == 0) return "delims=empty";
            int comma = 0;
            int tab = 0;
            int cr = 0;
            int lf = 0;
            int pipe = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == ',') comma++;
                else if (c == '\t') tab++;
                else if (c == '\r') cr++;
                else if (c == '\n') lf++;
                else if (c == '|') pipe++;
            }
            return "delims comma=" + comma + " tab=" + tab + " cr=" + cr + " lf=" + lf + " pipe=" + pipe;
        }

        private static void AppendMemory(string line)
        {
            _memory.Append(line);
            if (_memory.Length > MaxMemoryChars)
            {
                int cut = _memory.Length - (MaxMemoryChars / 2);
                if (cut < 0) cut = 0;
                _memory.Remove(0, cut);
                _memory.Insert(0, "...[memory trimmed]\r\n");
            }
        }

        private static void AppendFile(string line)
        {
            if (_filePath == null || _filePath.Length == 0)
            {
                _filePath = ResolveWritableLogPath();
            }
            try
            {
                string dir = Path.GetDirectoryName(_filePath);
                if (dir != null && dir.Length > 0 && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                TrimFileIfNeeded();
                CfCompat.AppendAllText(_filePath, line);
                _lastWriteError = "";
            }
            catch (Exception ex)
            {
                _lastWriteError = ex.Message;
            }
        }

        private static string ReadFileText()
        {
            if (_filePath == null || _filePath.Length == 0) return "";
            try
            {
                if (!File.Exists(_filePath)) return "";
                return CfCompat.ReadAllText(_filePath);
            }
            catch (Exception ex)
            {
                _lastWriteError = ex.Message;
                return "";
            }
        }

        private static string ResolveWritableLogPath()
        {
            string name = "merlin-debug.log";
            string[] dirs = {
                @"\Application Data\MerlinInventory",
                @"\Flash\MerlinInventory",
                @"\Storage Card\MerlinInventory",
                AppConfig.ConfigDirectory,
            };
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                if (dir == null || dir.Length == 0) continue;
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    string probe = Path.Combine(dir, ".write_test");
                    CfCompat.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return Path.Combine(dir, name);
                }
                catch { }
            }
            try
            {
                return Path.Combine(Path.GetTempPath(), name);
            }
            catch
            {
                return Path.Combine(AppConfig.ConfigDirectory, name);
            }
        }

        private static void TrimFileIfNeeded()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var fi = new FileInfo(_filePath);
                if (fi.Length <= MaxFileBytes) return;
                string text = CfCompat.ReadAllText(_filePath);
                if (text.Length > MaxFileBytes / 2)
                {
                    text = text.Substring(text.Length - MaxFileBytes / 2);
                }
                CfCompat.WriteAllText(_filePath, text);
            }
            catch { }
        }
    }
}
