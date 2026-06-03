using System;
using System.Collections;
using System.Text;

namespace MerlinAudit
{
    internal sealed class RssiSample
    {
        public long TimestampUtc;
        public int Rssi;
        public bool HasRssi;
        public bool Seen;
    }

    internal static class RssiTraceRecorder
    {
        private static readonly ArrayList Samples = new ArrayList();
        private static string _targetEpc = "";
        private static bool _recording;
        private static long _startedUtc;

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

        public static string TargetEpc
        {
            get { return _targetEpc ?? ""; }
        }

        public static void SetTarget(string epc)
        {
            _targetEpc = NormalizeEpc(epc);
        }

        public static void StartRecording()
        {
            Samples.Clear();
            _recording = true;
            _startedUtc = DateTime.UtcNow.Ticks / 10000L;
        }

        public static void StopRecording()
        {
            _recording = false;
            FlushToDisk();
        }

        public static void AddReading(int rssi, bool hasRssi, bool seen)
        {
            if (!_recording) return;
            var s = new RssiSample();
            s.TimestampUtc = DateTime.UtcNow.Ticks / 10000L;
            s.Rssi = rssi;
            s.HasRssi = hasRssi;
            s.Seen = seen;
            Samples.Add(s);
            if (Samples.Count % 8 == 0) FlushToDisk();
        }

        public static int SampleCount
        {
            get { return Samples.Count; }
        }

        public static void FlushToDisk()
        {
            try
            {
                string json = ToJson();
                if (json.Length > 0) CfCompat.WriteAllText(PendingFilePath, json);
            }
            catch { }
        }

        public static string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"format\":\"merlin-rssi-trace-v1\",\"target_epc\":\"");
            sb.Append(SimpleJson.Escape(_targetEpc ?? ""));
            sb.Append("\",\"started_at\":");
            sb.Append(_startedUtc);
            sb.Append(",\"stopped_at\":");
            sb.Append(DateTime.UtcNow.Ticks / 10000L);
            sb.Append(",\"samples\":[");
            for (int i = 0; i < Samples.Count; i++)
            {
                if (i > 0) sb.Append(",");
                RssiSample s = (RssiSample)Samples[i];
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
            sb.Append("]}");
            return sb.ToString();
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
