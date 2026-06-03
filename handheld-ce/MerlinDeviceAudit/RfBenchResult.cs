using System;
using System.Collections;
using System.Text;

namespace MerlinAudit
{
    internal sealed class RfBenchPulseSample
    {
        public int Pulse;
        public bool TargetSeen;
        public int TargetRssi;
        public bool TargetHasRssi;
        public int OtherTagCount;
        public string Mode = "round";
    }

    internal sealed class RfBenchPresetResult
    {
        public string PresetId = "";
        public string Label = "";
        public int LinkFreqHz;
        public int TxLevel;
        public bool UseEpcSelect;
        public bool ApplyOk;
        public int ReadLinkFreqHz = -1;
        public int ReadTxLevel = -1;
        public int Pulses;
        public int Hits;
        public int Misses;
        public int BestRssi = -999;
        public int WorstRssi = 0;
        public int RssiSum;
        public int RssiCount;
        public int OtherTagsTotal;
        public int Score;
        public readonly ArrayList PulseLog = new ArrayList();

        public int HitPercent
        {
            get
            {
                if (Pulses <= 0) return 0;
                return (Hits * 100) / Pulses;
            }
        }

        public int AvgRssi
        {
            get
            {
                if (RssiCount <= 0) return -999;
                return RssiSum / RssiCount;
            }
        }

        public void RecomputeScore()
        {
            int avg = AvgRssi;
            int avgPart = avg > -900 ? (avg + 100) : 0;
            Score = (Hits * 1000) + (HitPercent * 10) + avgPart;
        }
    }

    internal sealed class RfBenchDiagResult
    {
        public string TestId = "";
        public string Detail = "";
        public int Hits;
        public int Pulses;
        public int AvgRssi = -999;
    }

    internal static class RfBenchJson
    {
        public static string ToJson(
            string targetEpc,
            int pulsesPerPreset,
            RfBenchStackSetup stackSetup,
            ArrayList presetResults,
            ArrayList diagResults,
            string notes)
        {
            long now = DateTime.UtcNow.Ticks / 10000L;
            var sb = new StringBuilder(4096);
            sb.Append("{\"format\":\"merlin-rf-bench-v1\"");
            sb.Append(",\"target_epc\":\"").Append(SimpleJson.Escape(targetEpc ?? "")).Append('"');
            sb.Append(",\"pulses_per_preset\":").Append(pulsesPerPreset);
            sb.Append(",\"captured_at\":").Append(now);
            sb.Append(",\"app_version\":\"").Append(SimpleJson.Escape(AuditConfig.AppVersion)).Append('"');
            sb.Append(",\"notes\":\"").Append(SimpleJson.Escape(notes ?? "")).Append('"');
            if (stackSetup != null)
            {
                sb.Append(',');
                stackSetup.AppendJson(sb);
            }
            sb.Append(",\"bench_results\":[");
            AppendPresetResults(sb, presetResults);
            sb.Append("],\"diag_tests\":[");
            AppendDiagResults(sb, diagResults);
            sb.Append("],\"ranking\":[");
            AppendRanking(sb, presetResults);
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendPresetResults(StringBuilder sb, ArrayList results)
        {
            if (results == null || results.Count == 0) return;
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append(',');
                RfBenchPresetResult r = results[i] as RfBenchPresetResult;
                if (r == null) continue;
                r.RecomputeScore();
                sb.Append('{');
                sb.Append("\"preset_id\":\"").Append(SimpleJson.Escape(r.PresetId)).Append('"');
                sb.Append(",\"label\":\"").Append(SimpleJson.Escape(r.Label)).Append('"');
                sb.Append(",\"link_freq_hz\":").Append(r.LinkFreqHz);
                sb.Append(",\"tx_level\":").Append(r.TxLevel);
                sb.Append(",\"epc_select\":").Append(r.UseEpcSelect ? "true" : "false");
                sb.Append(",\"apply_ok\":").Append(r.ApplyOk ? "true" : "false");
                sb.Append(",\"read_link_freq_hz\":").Append(r.ReadLinkFreqHz);
                sb.Append(",\"read_tx_level\":").Append(r.ReadTxLevel);
                sb.Append(",\"pulses\":").Append(r.Pulses);
                sb.Append(",\"hits\":").Append(r.Hits);
                sb.Append(",\"misses\":").Append(r.Misses);
                sb.Append(",\"hit_pct\":").Append(r.HitPercent);
                sb.Append(",\"best_rssi\":").Append(r.BestRssi);
                sb.Append(",\"avg_rssi\":").Append(r.AvgRssi);
                sb.Append(",\"other_tags_total\":").Append(r.OtherTagsTotal);
                sb.Append(",\"score\":").Append(r.Score);
                sb.Append('}');
            }
        }

        private static void AppendDiagResults(StringBuilder sb, ArrayList results)
        {
            if (results == null || results.Count == 0) return;
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append(',');
                RfBenchDiagResult d = results[i] as RfBenchDiagResult;
                if (d == null) continue;
                sb.Append('{');
                sb.Append("\"test_id\":\"").Append(SimpleJson.Escape(d.TestId)).Append('"');
                sb.Append(",\"detail\":\"").Append(SimpleJson.Escape(d.Detail)).Append('"');
                sb.Append(",\"hits\":").Append(d.Hits);
                sb.Append(",\"pulses\":").Append(d.Pulses);
                sb.Append(",\"avg_rssi\":").Append(d.AvgRssi);
                sb.Append('}');
            }
        }

        private static void AppendRanking(StringBuilder sb, ArrayList results)
        {
            if (results == null || results.Count == 0) return;
            ArrayList sorted = new ArrayList();
            for (int i = 0; i < results.Count; i++)
            {
                RfBenchPresetResult r = results[i] as RfBenchPresetResult;
                if (r != null) sorted.Add(r);
            }
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    RfBenchPresetResult a = sorted[i] as RfBenchPresetResult;
                    RfBenchPresetResult b = sorted[j] as RfBenchPresetResult;
                    if (a == null || b == null) continue;
                    a.RecomputeScore();
                    b.RecomputeScore();
                    if (b.Score > a.Score)
                    {
                        sorted[i] = b;
                        sorted[j] = a;
                    }
                }
            }
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0) sb.Append(',');
                RfBenchPresetResult r = sorted[i] as RfBenchPresetResult;
                if (r == null) continue;
                sb.Append('"').Append(SimpleJson.Escape(r.PresetId)).Append('"');
            }
        }

        public static string PendingPath
        {
            get
            {
                return System.IO.Path.Combine(AuditConfig.ConfigDirectory, "pending-rf-bench.json");
            }
        }

        public static void SavePending(string json)
        {
            try
            {
                CfCompat.WriteAllText(PendingPath, json);
            }
            catch { }
        }

        public static string LoadPending()
        {
            try
            {
                if (!System.IO.File.Exists(PendingPath)) return "";
                return CfCompat.ReadAllText(PendingPath);
            }
            catch { }
            return "";
        }

        public static void ClearPending()
        {
            try
            {
                if (System.IO.File.Exists(PendingPath))
                {
                    System.IO.File.Delete(PendingPath);
                }
            }
            catch { }
        }
    }
}
