using System;
using System.IO;
using System.Text;

namespace MerlinHandheld
{
    public sealed class AppConfig
    {
        public const string AppVersion = "1.0.46+96c8284";
        private const string FileName = "merlin-handheld.cfg";

        public string ServerUrl = "http://10.17.17.17:3000";
        public string ScannerId = "merlin-handheld-01";
        public string LastBinId = "";
        public string LastMode = "Receive";

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
                string[] lines = File.ReadAllLines(ConfigPath);
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
            File.WriteAllText(ConfigPath, sb.ToString());
        }
    }
}
