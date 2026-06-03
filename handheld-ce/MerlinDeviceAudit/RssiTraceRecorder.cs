using System;
using System.Collections;
using System.Text;
using System.Threading;

namespace MerlinAudit
{
    internal sealed class RssiSample
    {
        public long TimestampUtc;
        public int Rssi;
        public bool HasRssi;
        public bool Seen;
    }

    internal sealed class RssiScanSnapshot
    {
        public long TimestampUtc;
        public RssiListEntry[] Tags = new RssiListEntry[0];
    }

    internal sealed class RssiDiagEvent
    {
        public long TimestampUtc;
        public string Kind = "";
        public string Detail = "";
    }

    internal static class RssiTraceRecorder
    {
        private static readonly ArrayList TrackSamples = new ArrayList();
        private static readonly ArrayList ScanSnapshots = new ArrayList();
        private static readonly ArrayList DiagEvents = new ArrayList();
        private const int MaxDiagEvents = 250;
        private static long _lastDiagKindTicks;
        private static string _lastDiagKind = "";
        private static string _targetEpc = "";
        private static bool _recording;
        private static long _startedUtc;
        private static long _lastActivityUtc;
        private static long _lastSnapTicks;
        private static string _lastSnapSig = "";
        private static long _lastFlushUtc;

        public static long SessionStartedUtc
        {
            get { return _startedUtc; }
        }

        public static long IdleMs
        {
            get
            {
                long now = DateTime.UtcNow.Ticks / 10000L;
                long idle = now - _lastActivityUtc;
                return idle < 0 ? 0 : idle;
            }
        }

        public static long SessionElapsedMs
        {
            get
            {
                if (_startedUtc <= 0) return 0;
                long elapsed = DateTime.UtcNow.Ticks / 10000L - _startedUtc;
                return elapsed < 0 ? 0 : elapsed;
            }
        }

        public static string PendingFilePath
        {
            get
            {
                return System.IO.Path.Combine(AuditConfig.ConfigDirectory, "pending-rssi-trace.json");
            }
        }

        public static bool IsRecording
        {
            get { return _recording; }
        }

        public static bool HasSessionData
        {
            get { return TrackSamples.Count > 0 || ScanSnapshots.Count > 0 || _startedUtc > 0; }
        }

        public static string TargetEpc
        {
            get { return _targetEpc ?? ""; }
        }

        public static int TrackSampleCount
        {
            get { return TrackSamples.Count; }
        }

        public static int ScanSnapshotCount
        {
            get { return ScanSnapshots.Count; }
        }

        public static long LastActivityUtc
        {
            get { return _lastActivityUtc; }
        }

        public static void SetTarget(string epc)
        {
            _targetEpc = NormalizeEpc(epc);
        }

        public static int DiagEventCount
        {
            get { return DiagEvents.Count; }
        }

        public static void StartRecording()
        {
            TrackSamples.Clear();
            ScanSnapshots.Clear();
            DiagEvents.Clear();
            _lastSnapSig = "";
            _lastSnapTicks = 0;
            _lastFlushUtc = 0;
            _recording = true;
            _startedUtc = DateTime.UtcNow.Ticks / 10000L;
            _lastActivityUtc = _startedUtc;
        }

        public static void PauseRecording()
        {
            if (!_recording) return;
            _recording = false;
            FlushToDiskAsync();
        }

        public static void ResumeRecording()
        {
            if (_startedUtc <= 0) _startedUtc = DateTime.UtcNow.Ticks / 10000L;
            _recording = true;
            _lastActivityUtc = DateTime.UtcNow.Ticks / 10000L;
        }

        public static void StopRecording()
        {
            _recording = false;
            FlushToDiskAsync();
        }

        public static void AddReading(int rssi, bool hasRssi, bool seen)
        {
            if (!_recording) return;
            var s = new RssiSample();
            s.TimestampUtc = DateTime.UtcNow.Ticks / 10000L;
            s.Rssi = rssi;
            s.HasRssi = hasRssi;
            s.Seen = seen;
            TrackSamples.Add(s);
            TouchActivity();
        }

        /// <summary>Record what the reader just saw (stream batch). Skips identical bursts within 80ms.</summary>
        public static void AddScanFromReadings(NurTagReading[] readings)
        {
            if (!_recording || readings == null || readings.Length == 0) return;
            RssiListEntry[] entries = RssiTagList.FromReadings(readings);
            AddScanSnapshotEntries(RssiTagList.SortAndCap(entries, RssiTagList.DefaultAccumulateMax));
        }

        public static void AddScanSnapshot(RssiListEntry[] tags)
        {
            AddScanSnapshotEntries(tags);
        }

        private static void AddScanSnapshotEntries(RssiListEntry[] tags)
        {
            if (!_recording || tags == null || tags.Length == 0) return;
            long now = DateTime.UtcNow.Ticks / 10000L;
            string sig = BuildSnapshotSig(tags);
            if (sig == _lastSnapSig && (now - _lastSnapTicks) < 80) return;
            _lastSnapSig = sig;
            _lastSnapTicks = now;

            var snap = new RssiScanSnapshot();
            snap.TimestampUtc = now;
            snap.Tags = tags;
            ScanSnapshots.Add(snap);
            TouchActivity();
            MaybeFlushToDisk();
        }

        private static string BuildSnapshotSig(RssiListEntry[] tags)
        {
            if (tags == null || tags.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == null) continue;
                sb.Append(tags[i].Epc);
                sb.Append('=');
                sb.Append(tags[i].HasRssi ? tags[i].Rssi.ToString() : "x");
                sb.Append(';');
            }
            return sb.ToString();
        }

        public static void AddDiag(string kind, string detail)
        {
            if (!_recording || kind == null) return;
            long now = DateTime.UtcNow.Ticks / 10000L;
            if (kind == "track_ok") { }
            else if (kind == _lastDiagKind && (now - _lastDiagKindTicks) < 1500) return;
            _lastDiagKind = kind;
            _lastDiagKindTicks = now;
            var d = new RssiDiagEvent();
            d.TimestampUtc = DateTime.UtcNow.Ticks / 10000L;
            d.Kind = kind.Length > 32 ? kind.Substring(0, 32) : kind;
            d.Detail = detail ?? "";
            if (d.Detail.Length > 120) d.Detail = d.Detail.Substring(0, 120);
            DiagEvents.Add(d);
            while (DiagEvents.Count > MaxDiagEvents)
            {
                DiagEvents.RemoveAt(0);
            }
            TouchActivity();
        }

        public static int SampleCount
        {
            get { return TrackSamples.Count + ScanSnapshots.Count; }
        }

        private static void TouchActivity()
        {
            _lastActivityUtc = DateTime.UtcNow.Ticks / 10000L;
        }

        private static void MaybeFlushToDisk()
        {
            long now = DateTime.UtcNow.Ticks / 10000L;
            if (now - _lastFlushUtc < 20000) return;
            _lastFlushUtc = now;
            FlushToDiskAsync();
        }

        public static void FlushToDiskAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string json = ToJson();
                    if (json.Length > 0) CfCompat.WriteAllText(PendingFilePath, json);
                }
                catch { }
            });
        }

        public static string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"format\":\"merlin-rssi-trace-v2.1\",\"target_epc\":\"");
            sb.Append(SimpleJson.Escape(_targetEpc ?? ""));
            sb.Append("\",\"started_at\":");
            sb.Append(_startedUtc);
            sb.Append(",\"stopped_at\":");
            sb.Append(DateTime.UtcNow.Ticks / 10000L);
            sb.Append(",\"last_activity_at\":");
            sb.Append(_lastActivityUtc);
            sb.Append(",\"scan_snapshots\":[");
            for (int i = 0; i < ScanSnapshots.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendScanJson(sb, (RssiScanSnapshot)ScanSnapshots[i]);
            }
            sb.Append("],\"track_samples\":[");
            for (int i = 0; i < TrackSamples.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendTrackJson(sb, (RssiSample)TrackSamples[i]);
            }
            sb.Append("],\"diag_events\":[");
            for (int i = 0; i < DiagEvents.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendDiagJson(sb, (RssiDiagEvent)DiagEvents[i]);
            }
            sb.Append("],\"samples\":[");
            for (int i = 0; i < TrackSamples.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendTrackJson(sb, (RssiSample)TrackSamples[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendTrackJson(StringBuilder sb, RssiSample s)
        {
            sb.Append("{\"t\":");
            sb.Append(s.TimestampUtc);
            sb.Append(",\"rssi\":");
            sb.Append(s.Rssi);
            sb.Append(",\"has_rssi\":");
            sb.Append(s.HasRssi ? "true" : "false");
            sb.Append(",\"seen\":");
            sb.Append(s.Seen ? "true" : "false");
            sb.Append("}");
        }

        private static void AppendDiagJson(StringBuilder sb, RssiDiagEvent d)
        {
            sb.Append("{\"t\":");
            sb.Append(d.TimestampUtc);
            sb.Append(",\"kind\":\"");
            sb.Append(SimpleJson.Escape(d.Kind ?? ""));
            sb.Append("\",\"detail\":\"");
            sb.Append(SimpleJson.Escape(d.Detail ?? ""));
            sb.Append("\"}");
        }

        private static void AppendScanJson(StringBuilder sb, RssiScanSnapshot snap)
        {
            sb.Append("{\"t\":");
            sb.Append(snap.TimestampUtc);
            sb.Append(",\"tags\":[");
            RssiListEntry[] tags = snap.Tags ?? new RssiListEntry[0];
            for (int i = 0; i < tags.Length; i++)
            {
                if (i > 0) sb.Append(",");
                RssiListEntry t = tags[i];
                sb.Append("{\"epc\":\"");
                sb.Append(SimpleJson.Escape(t.Epc ?? ""));
                sb.Append("\",\"rssi\":");
                sb.Append(t.Rssi);
                sb.Append(",\"has_rssi\":");
                sb.Append(t.HasRssi ? "true" : "false");
                sb.Append("}");
            }
            sb.Append("]}");
        }

        public static string LoadPendingJson()
        {
            try
            {
                if (!System.IO.File.Exists(PendingFilePath)) return "";
                return CfCompat.ReadAllText(PendingFilePath);
            }
            catch
            {
                return "";
            }
        }

        public static void ClearPending()
        {
            try
            {
                if (System.IO.File.Exists(PendingFilePath)) System.IO.File.Delete(PendingFilePath);
                AuditPendingQueue.ClearEntry("rssi_trace");
            }
            catch { }
        }

        public static string NormalizeEpc(string epc)
        {
            if (epc == null) return "";
            epc = epc.Trim().ToUpper();
            int comma = epc.IndexOf(',');
            if (comma > 0) epc = epc.Substring(0, comma);
            return epc;
        }
    }
}
