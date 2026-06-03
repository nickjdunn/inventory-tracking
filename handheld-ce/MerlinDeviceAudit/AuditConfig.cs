using System;
using System.IO;
using System.Text;

namespace MerlinAudit
{
    public sealed class AuditConfig
    {
        public const string AppVersion = "audit-1.8.6";
        private const string FileName = "merlin-audit.cfg";

        public string ServerUrl = "http://10.17.17.17:3000";
        public string ScannerId = "merlin-handheld-01";
        public bool HardwareNurEnabled = true;
        public string NurAssemblyPath = "";
        public int RfPresetIndex;
        public int BenchPulsesPerPreset = 8;
        public decimal BenchStackDistanceIn = 12m;
        public int BenchStackTagCount = 100;
        public decimal BenchStackSpacingIn = 0.02m;

        public static string ConfigDirectory
        {
            get
            {
                string dir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase);
                if (string.IsNullOrEmpty(dir)) dir = @"\";
                return dir;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, FileName); }
        }

        public static AuditConfig Load()
        {
            var cfg = new AuditConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return cfg;
                string[] lines = CfCompat.ReadAllLines(ConfigPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line == null) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLower();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "server") cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(val);
                    else if (key == "scanner") cfg.ScannerId = val;
                    else if (key == "nur_dll") cfg.NurAssemblyPath = val;
                    else if (key == "rf_preset") cfg.RfPresetIndex = ParseInt(val, 0);
                    else if (key == "bench_pulses") cfg.BenchPulsesPerPreset = ParseInt(val, 8);
                    else if (key == "bench_dist_in") cfg.BenchStackDistanceIn = ParseDecimal(val, 12m);
                    else if (key == "bench_tag_count") cfg.BenchStackTagCount = ParseInt(val, 100);
                    else if (key == "bench_spacing_in") cfg.BenchStackSpacingIn = ParseDecimal(val, 0.02m);
                }
            }
            catch { }
            return cfg;
        }

        public void Save()
        {
            ServerUrl = HttpHelper.NormalizeBaseUrl(ServerUrl);
            var sb = new System.Text.StringBuilder();
            sb.Append("server=").Append(ServerUrl).Append("\r\n");
            sb.Append("scanner=").Append(ScannerId ?? "").Append("\r\n");
            if (NurAssemblyPath != null && NurAssemblyPath.Length > 0)
            {
                sb.Append("nur_dll=").Append(NurAssemblyPath).Append("\r\n");
            }
            sb.Append("rf_preset=").Append(RfPresetIndex).Append("\r\n");
            sb.Append("bench_pulses=").Append(BenchPulsesPerPreset).Append("\r\n");
            sb.Append("bench_dist_in=").Append(RfBenchStackSetup.FormatDecimal(BenchStackDistanceIn)).Append("\r\n");
            sb.Append("bench_tag_count=").Append(BenchStackTagCount).Append("\r\n");
            sb.Append("bench_spacing_in=").Append(RfBenchStackSetup.FormatDecimal(BenchStackSpacingIn)).Append("\r\n");
            CfCompat.WriteAllText(ConfigPath, sb.ToString());
        }

        private static int ParseInt(string val, int fallback)
        {
            try
            {
                return int.Parse(val);
            }
            catch
            {
                return fallback;
            }
        }

        private static decimal ParseDecimal(string val, decimal fallback)
        {
            decimal d;
            if (RfBenchStackSetup.TryParseDecimal(val, out d)) return d;
            return fallback;
        }
    }
}
