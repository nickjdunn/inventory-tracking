using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;

namespace MerlinAudit
{
    internal sealed class NurDllCandidate
    {
        public string Path = "";
        public string Name = "";
        public long SizeBytes;
        public bool DotNetLoadable;
        public string Note = "";
    }

    internal sealed class NurDllDiscoveryResult
    {
        public readonly ArrayList Candidates = new ArrayList();
        public string BestPath = "";
        public string InstalledBesideApp = "";
        public string ServerFetchUrl = "";
        public string ServerFetchError = "";
    }

    /// <summary>Finds NurApi*.dll on device, tests .NET load, copies best beside app, can fetch from server.</summary>
    internal static class NurDllDiscovery
    {
        public const string ServerFileNameWce = "NurApiDotNetWCE.dll";
        public const string ServerFileNameDesktop = "NurApiDotNet.dll";

        public static NurDllDiscoveryResult Discover(ArrayList installedFiles, AuditConfig cfg)
        {
            var result = new NurDllDiscoveryResult();
            var seen = new Hashtable();

            CollectFromInstalled(installedFiles, result, seen);
            CollectFromFilesystem(result, seen);
            PickBest(result);

            if (result.BestPath.Length > 0)
            {
                result.InstalledBesideApp = TryCopyBesideApp(result.BestPath);
                if (cfg != null && result.InstalledBesideApp.Length > 0)
                {
                    cfg.NurAssemblyPath = result.InstalledBesideApp;
                    try { cfg.Save(); } catch { }
                }
            }

            return result;
        }

        public static void TryFetchFromServer(AuditConfig cfg, NurDllDiscoveryResult result)
        {
            if (cfg == null || result == null) return;
            string[] names = new string[] { ServerFileNameWce, ServerFileNameDesktop };
            string baseUrl = HttpHelper.NormalizeBaseUrl(cfg.ServerUrl) + "/deploy/nur/";

            for (int n = 0; n < names.Length; n++)
            {
                string dest = Path.Combine(AuditConfig.ConfigDirectory, names[n]);
                if (File.Exists(dest) && new FileInfo(dest).Length > 1024 && TestDotNetLoad(dest))
                {
                    result.ServerFetchUrl = dest;
                    SaveNurPath(cfg, dest);
                    return;
                }

                string url = baseUrl + names[n];
                if (HttpHelper.DownloadToFile(url, dest, 90000) && TestDotNetLoad(dest))
                {
                    result.ServerFetchUrl = url;
                    result.ServerFetchError = "";
                    SaveNurPath(cfg, dest);
                    return;
                }
            }

            result.ServerFetchError = "not on server (optional — gun has \\Windows\\NurApiDotNetWCE.dll)";
        }

        private static void SaveNurPath(AuditConfig cfg, string dest)
        {
            if (cfg == null || dest == null || dest.Length == 0) return;
            cfg.NurAssemblyPath = dest;
            try { cfg.Save(); } catch { }
        }

        public static void AppendJson(StringBuilder sb, NurDllDiscoveryResult result)
        {
            sb.Append("{");
            AuditJson.AppendString(sb, "server_filename_wce", ServerFileNameWce);
            sb.Append(",");
            AuditJson.AppendString(sb, "server_filename", ServerFileNameDesktop);
            sb.Append(",");
            AuditJson.AppendString(sb, "best_path", result != null ? result.BestPath : "");
            sb.Append(",");
            AuditJson.AppendString(sb, "installed_beside_app", result != null ? result.InstalledBesideApp : "");
            sb.Append(",");
            AuditJson.AppendString(sb, "server_fetch_url", result != null ? result.ServerFetchUrl : "");
            sb.Append(",");
            AuditJson.AppendString(sb, "server_fetch_error", result != null ? result.ServerFetchError : "");
            sb.Append(",");
            AuditJson.AppendBool(sb, "dotnet_ready", result != null && HasLoadable(result));
            sb.Append(",");
            sb.Append("\"candidates\":[");
            if (result != null)
            {
                for (int i = 0; i < result.Candidates.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendCandidate(sb, (NurDllCandidate)result.Candidates[i]);
                }
            }
            sb.Append("]}");
        }

        private static bool HasLoadable(NurDllDiscoveryResult result)
        {
            for (int i = 0; i < result.Candidates.Count; i++)
            {
                if (((NurDllCandidate)result.Candidates[i]).DotNetLoadable) return true;
            }
            return result.InstalledBesideApp.Length > 0;
        }

        private static void AppendCandidate(StringBuilder sb, NurDllCandidate c)
        {
            sb.Append("{");
            AuditJson.AppendString(sb, "path", c.Path);
            sb.Append(",");
            AuditJson.AppendString(sb, "name", c.Name);
            sb.Append(",");
            AuditJson.AppendLong(sb, "size_bytes", c.SizeBytes);
            sb.Append(",");
            AuditJson.AppendBool(sb, "dotnet_loadable", c.DotNetLoadable);
            sb.Append(",");
            AuditJson.AppendString(sb, "note", c.Note);
            sb.Append("}");
        }

        private static void CollectFromInstalled(ArrayList installedFiles, NurDllDiscoveryResult result, Hashtable seen)
        {
            if (installedFiles == null) return;
            for (int i = 0; i < installedFiles.Count; i++)
            {
                FileEntry fe = (FileEntry)installedFiles[i];
                if (fe == null || fe.Path == null) continue;
                if (!fe.Path.ToLower().EndsWith(".dll")) continue;
                AddCandidate(result, fe.Path, seen);
            }
        }

        private static void CollectFromFilesystem(NurDllDiscoveryResult result, Hashtable seen)
        {
            string[] roots = new string[]
            {
                @"\Program Files",
                @"\Flash",
                @"\Application",
                @"\Windows",
                AuditConfig.ConfigDirectory,
            };

            for (int r = 0; r < roots.Length; r++)
            {
                string root = roots[r];
                if (root == null || !Directory.Exists(root)) continue;
                WalkDlls(root, 0, 5, result, seen);
            }
        }

        private static void WalkDlls(string dir, int depth, int maxDepth, NurDllDiscoveryResult result, Hashtable seen)
        {
            if (depth > maxDepth) return;
            try
            {
                string[] files = Directory.GetFiles(dir, "*.dll");
                for (int i = 0; i < files.Length; i++)
                {
                    AddCandidate(result, files[i], seen);
                }
                if (depth >= maxDepth) return;
                string[] subs = Directory.GetDirectories(dir);
                for (int s = 0; s < subs.Length; s++)
                {
                    WalkDlls(subs[s], depth + 1, maxDepth, result, seen);
                }
            }
            catch { }
        }

        private static void AddCandidate(NurDllDiscoveryResult result, string path, Hashtable seen)
        {
            if (path == null || path.Length == 0) return;
            string key = path.ToLower();
            if (seen.Contains(key)) return;

            string name = Path.GetFileName(path).ToLower();
            if (name.IndexOf("nur") < 0) return;
            if (name.IndexOf("nurser") >= 0) return;

            seen[key] = true;
            var c = new NurDllCandidate();
            c.Path = path;
            c.Name = Path.GetFileName(path);
            try
            {
                c.SizeBytes = new FileInfo(path).Length;
            }
            catch { }

            if (!NurApiReflection.IsManagedNurDllFile(c.Name))
            {
                c.Note = "not NurApiDotNet wrapper";
                result.Candidates.Add(c);
                return;
            }

            if (TestDotNetLoad(path))
            {
                c.DotNetLoadable = true;
                c.Note = ".NET NurApi OK";
            }
            else if (name.IndexOf("nurapi") >= 0 || name.IndexOf("nur") >= 0)
            {
                c.Note = "native or wrong CLR — not loadable as .NET NurApi";
            }

            result.Candidates.Add(c);
        }

        private static bool TestDotNetLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                if (!NurApiReflection.IsManagedNurDllFile(Path.GetFileName(path))) return false;
                Assembly asm = Assembly.LoadFrom(path);
                return NurApiReflection.AssemblyHasApiType(asm);
            }
            catch
            {
                return false;
            }
        }

        private static void PickBest(NurDllDiscoveryResult result)
        {
            for (int i = 0; i < result.Candidates.Count; i++)
            {
                NurDllCandidate c = (NurDllCandidate)result.Candidates[i];
                if (c.DotNetLoadable)
                {
                    result.BestPath = c.Path;
                    return;
                }
            }
            for (int i = 0; i < result.Candidates.Count; i++)
            {
                NurDllCandidate c = (NurDllCandidate)result.Candidates[i];
                if (c.Name.ToLower().IndexOf("nurapi") >= 0 && result.BestPath.Length == 0)
                {
                    result.BestPath = c.Path;
                }
            }
        }

        private static string TryCopyBesideApp(string sourcePath)
        {
            try
            {
                string dest = Path.Combine(AuditConfig.ConfigDirectory, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, dest, true);
                return dest;
            }
            catch
            {
                return "";
            }
        }
    }
}
