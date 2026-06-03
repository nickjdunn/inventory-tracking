using System;
using System.Collections;
using System.IO;
using System.Text;

namespace MerlinAudit
{
    public sealed class DeviceAuditCollector
    {
        private const int MaxFiles = 600;
        private const int MaxConfigBytes = 8192;
        private const int MaxConfigFiles = 24;

        private readonly AuditConfig _cfg;
        private int _fileCount;

        public DeviceAuditCollector(AuditConfig cfg)
        {
            _cfg = cfg;
        }

        public string CollectReportJson(string scanSessionJson)
        {
            _fileCount = 0;
            var installed = new ArrayList();
            var roots = new ArrayList();
            var configs = new ArrayList();

            string[] rootPaths = new string[]
            {
                @"\Program Files",
                @"\Windows",
                @"\Flash",
                @"\Application",
                @"\Temp",
            };

            for (int i = 0; i < rootPaths.Length; i++)
            {
                CollectRootSummary(rootPaths[i], roots);
                WalkDirectory(rootPaths[i], 0, 4, installed);
            }

            CollectStartMenu(installed);
            CollectConfigSnippets(configs);
            ArrayList known = KnownAppsCatalog.MatchInstalledFiles(installed);
            string networkJson = CollectNetworkJson();

            var sb = new StringBuilder();
            sb.Append("{");
            AuditJson.AppendString(sb, "format", "merlin-device-audit-v1");
            sb.Append(",");
            AuditJson.AppendLong(sb, "captured_at", DateTime.UtcNow.Ticks / 10000L);
            sb.Append(",");
            AuditJson.AppendString(sb, "scanner_id", _cfg.ScannerId ?? "");
            sb.Append(",");
            AuditJson.AppendString(sb, "app_version", AuditConfig.AppVersion);
            sb.Append(",");
            AuditJson.AppendString(sb, "server_url", _cfg.ServerUrl ?? "");
            sb.Append(",");
            AuditJson.AppendObject(sb, "system", BuildSystemObject());
            sb.Append(",");
            AuditJson.AppendArray(sb, "storage_roots", BuildRootsArray(roots));
            sb.Append(",");
            AuditJson.AppendArray(sb, "installed_files", BuildFilesArray(installed));
            sb.Append(",");
            AuditJson.AppendArray(sb, "known_apps", BuildKnownAppsArray(known));
            sb.Append(",");
            AuditJson.AppendArray(sb, "config_snippets", BuildConfigsArray(configs));
            sb.Append(",");
            if (scanSessionJson != null && scanSessionJson.Length > 0)
            {
                sb.Append("\"scan_session\":");
                sb.Append(scanSessionJson);
            }
            else
            {
                sb.Append("\"scan_session\":{\"format\":\"merlin-scan-guide-v1\",\"completed\":false,\"steps_total\":7,\"captures\":0,\"events\":[]}");
            }
            sb.Append(",");
            AuditJson.AppendObject(sb, "network", networkJson);
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildSystemObject()
        {
            var sb = new StringBuilder();
            CeSystemInfo.AppendSystemJson(sb);
            return sb.ToString();
        }

        private string CollectNetworkJson()
        {
            var sb = new StringBuilder();
            AuditJson.AppendString(sb, "server_url", _cfg.ServerUrl ?? "");
            sb.Append(",");
            AuditJson.AppendBool(sb, "ping_ok", false);
            sb.Append(",");
            AuditJson.AppendString(sb, "ping_error", "");

            try
            {
                string pingUrl = HttpHelper.NormalizeBaseUrl(_cfg.ServerUrl) + "/api/ping";
                HttpResult res = HttpHelper.Get(pingUrl, 12000);
                sb.Length = 0;
                AuditJson.AppendString(sb, "server_url", _cfg.ServerUrl ?? "");
                sb.Append(",");
                AuditJson.AppendBool(sb, "ping_ok", res.Ok);
                sb.Append(",");
                AuditJson.AppendString(sb, "ping_error", res.Ok ? "" : (res.Error ?? ""));
                if (res.Ok)
                {
                    sb.Append(",\"server_version\":\"").Append(SimpleJson.Escape(SimpleJson.ExtractString(res.Body, "version"))).Append("\"");
                }
            }
            catch (Exception ex)
            {
                sb.Length = 0;
                AuditJson.AppendString(sb, "server_url", _cfg.ServerUrl ?? "");
                sb.Append(",");
                AuditJson.AppendBool(sb, "ping_ok", false);
                sb.Append(",");
                AuditJson.AppendString(sb, "ping_error", ex.Message ?? "");
            }
            return sb.ToString();
        }

        private void CollectRootSummary(string root, ArrayList into)
        {
            var entry = new RootSummary();
            entry.Path = root;
            try
            {
                if (!Directory.Exists(root))
                {
                    entry.Exists = false;
                    into.Add(entry);
                    return;
                }
                entry.Exists = true;
                string[] dirs = Directory.GetDirectories(root);
                string[] files = Directory.GetFiles(root);
                entry.TopLevelDirs = dirs != null ? dirs.Length : 0;
                entry.TopLevelFiles = files != null ? files.Length : 0;
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message ?? "";
            }
            into.Add(entry);
        }

        private void CollectStartMenu(ArrayList into)
        {
            string[] menus = new string[]
            {
                @"\Windows\Start Menu",
                @"\Windows\Start Menu\Programs",
            };
            for (int m = 0; m < menus.Length; m++)
            {
                WalkDirectory(menus[m], 0, 3, into);
            }
        }

        private void CollectConfigSnippets(ArrayList into)
        {
            if (into.Count >= MaxConfigFiles) return;

            TryAddConfig(into, AuditConfig.ConfigPath, "merlin-audit.cfg");
            TryAddConfig(into, Path.Combine(AuditConfig.ConfigDirectory, "merlin-handheld.cfg"), "merlin-handheld.cfg");

            string[] guesses = new string[]
            {
                @"\Program Files\MerlinInventory\merlin-handheld.cfg",
                @"\Program Files\MerlinAudit\merlin-audit.cfg",
                @"\Flash\merlin-handheld.cfg",
            };
            for (int i = 0; i < guesses.Length; i++)
            {
                TryAddConfig(into, guesses[i], Path.GetFileName(guesses[i]));
            }
        }

        private static void TryAddConfig(ArrayList into, string path, string label)
        {
            if (into.Count >= MaxConfigFiles) return;
            try
            {
                if (!File.Exists(path)) return;
                string text = CfCompat.ReadAllText(path);
                if (text.Length > MaxConfigBytes)
                {
                    text = text.Substring(0, MaxConfigBytes) + "\r\n...[truncated]";
                }
                var cfg = new ConfigSnippet();
                cfg.Label = label;
                cfg.Path = path;
                cfg.Text = text;
                into.Add(cfg);
            }
            catch { }
        }

        private void WalkDirectory(string dir, int depth, int maxDepth, ArrayList into)
        {
            if (_fileCount >= MaxFiles) return;
            if (depth > maxDepth) return;
            try
            {
                if (!Directory.Exists(dir)) return;

                string[] files = Directory.GetFiles(dir);
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (_fileCount >= MaxFiles) return;
                        AddFileEntry(files[i], into);
                    }
                }

                if (depth >= maxDepth) return;
                string[] dirs = Directory.GetDirectories(dir);
                if (dirs == null) return;
                for (int d = 0; d < dirs.Length; d++)
                {
                    if (_fileCount >= MaxFiles) return;
                    WalkDirectory(dirs[d], depth + 1, maxDepth, into);
                }
            }
            catch { }
        }

        private void AddFileEntry(string fullPath, ArrayList into)
        {
            try
            {
                string name = Path.GetFileName(fullPath);
                if (name == null || name.Length == 0) return;
                string ext = Path.GetExtension(name);
                if (ext == null) ext = "";
                ext = ext.ToLower();
                if (ext != ".exe" && ext != ".dll" && ext != ".lnk" && ext != ".cab" && ext != ".cfg" && ext != ".ini")
                {
                    return;
                }

                FileInfo info = new FileInfo(fullPath);
                var fe = new FileEntry();
                fe.Path = fullPath;
                fe.Name = name;
                fe.Ext = ext;
                fe.SizeBytes = info.Length;
                fe.ModifiedUtc = info.LastWriteTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                fe.Kind = ext.TrimStart('.');
                into.Add(fe);
                _fileCount++;
            }
            catch { }
        }

        private static string BuildRootsArray(ArrayList roots)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < roots.Count; i++)
            {
                if (i > 0) sb.Append(",");
                RootSummary r = (RootSummary)roots[i];
                sb.Append("{");
                AuditJson.AppendString(sb, "path", r.Path);
                sb.Append(",");
                AuditJson.AppendBool(sb, "exists", r.Exists);
                sb.Append(",\"top_dirs\":").Append(r.TopLevelDirs);
                sb.Append(",\"top_files\":").Append(r.TopLevelFiles);
                if (r.Error != null && r.Error.Length > 0)
                {
                    sb.Append(",\"error\":\"").Append(SimpleJson.Escape(r.Error)).Append("\"");
                }
                sb.Append("}");
            }
            return sb.ToString();
        }

        private static string BuildFilesArray(ArrayList files)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < files.Count; i++)
            {
                if (i > 0) sb.Append(",");
                FileEntry fe = (FileEntry)files[i];
                sb.Append("{");
                AuditJson.AppendString(sb, "path", fe.Path);
                sb.Append(",");
                AuditJson.AppendString(sb, "name", fe.Name);
                sb.Append(",");
                AuditJson.AppendString(sb, "ext", fe.Ext);
                sb.Append(",");
                AuditJson.AppendLong(sb, "size_bytes", fe.SizeBytes);
                sb.Append(",");
                AuditJson.AppendString(sb, "modified_utc", fe.ModifiedUtc);
                sb.Append(",");
                AuditJson.AppendString(sb, "kind", fe.Kind);
                sb.Append("}");
            }
            return sb.ToString();
        }

        private static string BuildKnownAppsArray(ArrayList known)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < known.Count; i++)
            {
                if (i > 0) sb.Append(",");
                KnownAppHit hit = (KnownAppHit)known[i];
                sb.Append("{");
                AuditJson.AppendString(sb, "name", hit.Name);
                sb.Append(",");
                AuditJson.AppendString(sb, "role", hit.Role);
                sb.Append(",");
                AuditJson.AppendString(sb, "path", hit.Path);
                sb.Append(",");
                AuditJson.AppendLong(sb, "size_bytes", hit.SizeBytes);
                sb.Append("}");
            }
            return sb.ToString();
        }

        private static string BuildConfigsArray(ArrayList configs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < configs.Count; i++)
            {
                if (i > 0) sb.Append(",");
                ConfigSnippet c = (ConfigSnippet)configs[i];
                sb.Append("{");
                AuditJson.AppendString(sb, "label", c.Label);
                sb.Append(",");
                AuditJson.AppendString(sb, "path", c.Path);
                sb.Append(",");
                AuditJson.AppendString(sb, "text", c.Text);
                sb.Append("}");
            }
            return sb.ToString();
        }

        private sealed class RootSummary
        {
            public string Path = "";
            public bool Exists;
            public int TopLevelDirs;
            public int TopLevelFiles;
            public string Error = "";
        }

        private sealed class ConfigSnippet
        {
            public string Label = "";
            public string Path = "";
            public string Text = "";
        }
    }
}
