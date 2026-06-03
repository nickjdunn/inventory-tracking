using System;
using System.Collections;
using System.Text;

namespace MerlinAudit
{
    internal sealed class RfBenchPulseSample
    {
        public int Pulse;
        public int TagCount;
        public int BestRssi = -999;
        public int AvgRssi = -999;
        public bool TargetSeen;
        public int TargetRssi;
        public string Mode = "pile_open";
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
        /// <summary>Pulses that read at least one tag in the pile.</summary>
        public int Hits;
        public int Misses;
        public int TagsReadTotal;
        public int TargetHits;
        public int BestRssi = -999;
        public int WorstRssi = 0;
        public int RssiSum;
        public int RssiCount;
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

        public int AvgTagsPerPulse
        {
            get
            {
                if (Pulses <= 0) return 0;
                return TagsReadTotal / Pulses;
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
            int avgTags = AvgTagsPerPulse;
            int avgPart = AvgRssi > -900 ? (AvgRssi + 100) : 0;
            int bestPart = BestRssi > -900 ? (BestRssi + 120) : 0;
            Score = (Hits * 600) + (avgTags * 35) + avgPart + bestPart + (TargetHits * 150);
        }
    }

    internal sealed class RfBenchDiagResult
    {
        public string TestId = "";
        public string Detail = "";
        public int Hits;
        public int Pulses;
        public int AvgTags;
        public int AvgRssi = -999;
    }

    internal static class RfBenchPileMetrics
    {
        public static void RecordRound(
            NurTagReading[] all,
            string optionalTargetEpc,
            RfBenchPresetResult result,
            RfBenchPulseSample sample,
            string mode)
        {
            sample.Mode = mode;
            if (all == null || all.Length == 0)
            {
                sample.TagCount = 0;
                result.Misses++;
                return;
            }

            sample.TagCount = all.Length;
            result.TagsReadTotal += all.Length;
            result.Hits++;

            int rssiSum = 0;
            int rssiN = 0;
            int best = -999;
            int worst = 0;
            string normTarget = RssiTraceRecorder.NormalizeEpc(optionalTargetEpc);

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (!all[i].HasRssi) continue;
                int r = all[i].Rssi;
                rssiSum += r;
                rssiN++;
                if (r > best) best = r;
                if (rssiN == 1 || r < worst) worst = r;

                if (normTarget.Length > 0)
                {
                    string e = RssiTraceRecorder.NormalizeEpc(all[i].Epc);
                    if (e == normTarget)
                    {
                        sample.TargetSeen = true;
                        sample.TargetRssi = r;
                    }
                }
            }

            if (rssiN > 0)
            {
                sample.BestRssi = best;
                sample.AvgRssi = rssiSum / rssiN;
                result.RssiSum += rssiSum;
                result.RssiCount += rssiN;
                if (best > result.BestRssi) result.BestRssi = best;
                if (result.WorstRssi == 0 || worst < result.WorstRssi) result.WorstRssi = worst;
            }

            if (sample.TargetSeen) result.TargetHits++;
        }
    }

    internal static class RfBenchJson
    {
        public static string ToJson(
            string benchMode,
            string optionalTargetEpc,
            int pulsesPerPreset,
            RfBenchStackSetup stackSetup,
            ArrayList presetResults,
            ArrayList diagResults,
            string notes)
        {
            long now = DateTime.UtcNow.Ticks / 10000L;
            var sb = new StringBuilder(4096);
            sb.Append("{\"format\":\"merlin-rf-bench-v1\"");
            sb.Append(",\"bench_mode\":\"").Append(SimpleJson.Escape(benchMode ?? "pile")).Append('"');
            sb.Append(",\"target_epc\":\"").Append(SimpleJson.Escape(optionalTargetEpc ?? "")).Append('"');
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
                sb.Append(",\"round_hits\":").Append(r.Hits);
                sb.Append(",\"misses\":").Append(r.Misses);
                sb.Append(",\"hit_pct\":").Append(r.HitPercent);
                sb.Append(",\"tags_read_total\":").Append(r.TagsReadTotal);
                sb.Append(",\"avg_tags_per_pulse\":").Append(r.AvgTagsPerPulse);
                sb.Append(",\"target_hits\":").Append(r.TargetHits);
                sb.Append(",\"best_rssi\":").Append(r.BestRssi);
                sb.Append(",\"avg_rssi\":").Append(r.AvgRssi);
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
                sb.Append(",\"avg_tags\":").Append(d.AvgTags);
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
