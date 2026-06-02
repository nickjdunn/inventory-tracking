using System;
using System.IO;
using System.Text;

namespace MerlinHandheld
{
    public sealed class AppConfig
    {
        public const string AppVersion = "1.0.53+b897e15";
        private const string FileName = "merlin-handheld.cfg";

        public string ServerUrl = "http://10.17.17.17:3000";
        public string ScannerId = "merlin-handheld-01";
        public string LastBinId = "";
        public string LastMode = "Receive";
        public bool HardwareNurEnabled = true;
        public bool HardwareWedgeEnabled = true;
        public string NurAssemblyPath = "";
        public bool LiveRawStream = true;
        public bool LiveScanStream = false;
        public string LiveScanBinId = "";
        public bool DiagnosticLogFile = false;

        public static string ConfigDirectory
        {
            get
            {
                string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase);
                if (string.IsNullOrEmpty(dir)) dir = @"\";
                return dir;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, FileName); }
        }

        public static AppConfig Load()
        {
            var cfg = new AppConfig();
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
                    else if (key == "bin") cfg.LastBinId = val;
                    else if (key == "mode") cfg.LastMode = val;
                    else if (key == "nur") cfg.HardwareNurEnabled = val == "1" || val.ToLower() == "true";
                    else if (key == "wedge") cfg.HardwareWedgeEnabled = val == "1" || val.ToLower() == "true";
                    else if (key == "nur_dll") cfg.NurAssemblyPath = val;
                    else if (key == "live_raw") cfg.LiveRawStream = val == "1" || val.ToLower() == "true";
                    else if (key == "live_scan") cfg.LiveScanStream = val == "1" || val.ToLower() == "true";
                    else if (key == "live_bin") cfg.LiveScanBinId = val;
                    else if (key == "diag_log") cfg.DiagnosticLogFile = val == "1" || val.ToLower() == "true";
                }
            }
            catch { }
            return cfg;
        }

        public void Save()
        {
            ServerUrl = HttpHelper.NormalizeBaseUrl(ServerUrl);
            var sb = new StringBuilder();
            sb.AppendLine("server=" + ServerUrl);
            sb.AppendLine("scanner=" + ScannerId);
            sb.AppendLine("bin=" + (LastBinId ?? ""));
            sb.AppendLine("mode=" + (LastMode ?? "Receive"));
            sb.AppendLine("nur=" + (HardwareNurEnabled ? "1" : "0"));
            sb.AppendLine("wedge=" + (HardwareWedgeEnabled ? "1" : "0"));
            if (NurAssemblyPath != null && NurAssemblyPath.Length > 0)
            {
                sb.AppendLine("nur_dll=" + NurAssemblyPath);
            }
            sb.AppendLine("live_raw=" + (LiveRawStream ? "1" : "0"));
            sb.AppendLine("live_scan=" + (LiveScanStream ? "1" : "0"));
            sb.AppendLine("live_bin=" + (LiveScanBinId ?? ""));
            sb.AppendLine("diag_log=" + (DiagnosticLogFile ? "1" : "0"));
            CfCompat.WriteAllText(ConfigPath, sb.ToString());
        }
    }
}
