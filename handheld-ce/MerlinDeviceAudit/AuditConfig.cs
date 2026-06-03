using System;
using System.IO;
using System.Text;

namespace MerlinAudit
{
    public sealed class AuditConfig
    {
        public const string AppVersion = "audit-1.3.0";
        private const string FileName = "merlin-audit.cfg";

        public string ServerUrl = "http://10.17.17.17:3000";
        public string ScannerId = "merlin-handheld-01";
        public bool HardwareNurEnabled = true;
        public string NurAssemblyPath = "";

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
            CfCompat.WriteAllText(ConfigPath, sb.ToString());
        }
    }
}
