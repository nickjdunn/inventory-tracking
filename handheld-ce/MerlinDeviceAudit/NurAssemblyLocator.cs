using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace MerlinAudit
{
    /// <summary>Finds NurApi*.dll on CE devices where install paths vary by Nordic package.</summary>
    internal static class NurAssemblyLocator
    {
        public static string LastSummary = "";

        private static readonly string[] BootstrapPaths = new string[]
        {
            @"\Windows\NurApiDotNetWCE.dll",
            @"\Program Files\MerlinAudit\NurApiDotNetWCE.dll",
            @"\Program Files\MerlinInventory\NurApiDotNetWCE.dll",
            @"\Windows\NurApiDotNet.dll",
            @"\Program Files\Nordic ID\NUR API\NurApiDotNet.dll",
            @"\Program Files\NordicId\NurApi\NurApiDotNet.dll",
        };

        public static Assembly LoadNurAssembly(AuditConfig cfg)
        {
            LastSummary = "";
            var tried = new ArrayList();

            if (cfg != null && cfg.NurAssemblyPath != null && cfg.NurAssemblyPath.Length > 0)
            {
                Assembly asm = TryLoadPath(cfg.NurAssemblyPath, tried);
                if (asm != null) return asm;
            }

            for (int i = 0; i < BootstrapPaths.Length; i++)
            {
                Assembly asm = TryLoadPath(BootstrapPaths[i], tried);
                if (asm != null) return asm;
            }

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

                string[] shallow = Directory.GetFiles(root, "*.dll");
                for (int i = 0; i < shallow.Length; i++)
                {
                    Assembly asm = TryLoadIfNur(shallow[i], tried);
                    if (asm != null) return asm;
                }

                Assembly deep = WalkForNur(root, 0, 5, tried);
                if (deep != null) return deep;
            }

            LastSummary = "searched " + tried.Count + " dll(s), none loaded";
            if (tried.Count > 0)
            {
                int show = tried.Count > 3 ? 3 : tried.Count;
                LastSummary += " e.g. " + (string)tried[tried.Count - show];
            }
            return null;
        }

        private static Assembly WalkForNur(string dir, int depth, int maxDepth, ArrayList tried)
        {
            if (depth > maxDepth) return null;

            try
            {
                string[] files = Directory.GetFiles(dir, "*.dll");
                for (int i = 0; i < files.Length; i++)
                {
                    Assembly asm = TryLoadIfNur(files[i], tried);
                    if (asm != null) return asm;
                }

                if (depth >= maxDepth) return null;
                string[] subs = Directory.GetDirectories(dir);
                for (int s = 0; s < subs.Length; s++)
                {
                    Assembly asm = WalkForNur(subs[s], depth + 1, maxDepth, tried);
                    if (asm != null) return asm;
                }
            }
            catch { }

            return null;
        }

        private static Assembly TryLoadIfNur(string path, ArrayList tried)
        {
            if (path == null) return null;
            string name = Path.GetFileName(path);
            if (!NurApiReflection.IsManagedNurDllFile(name)) return null;
            return TryLoadPath(path, tried);
        }

        private static Assembly TryLoadPath(string path, ArrayList tried)
        {
            if (path == null || path.Length == 0) return null;
            tried.Add(path);
            try
            {
                if (!File.Exists(path)) return null;
                Assembly asm = Assembly.LoadFrom(path);
                if (NurApiReflection.AssemblyHasApiType(asm))
                {
                    LastSummary = "loaded " + path + " (" + NurApiReflection.LastResolvedTypeName + ")";
                    return asm;
                }
            }
            catch { }
            return null;
        }

        public static bool HasNurApiTypePublic(Assembly asm)
        {
            return NurApiReflection.AssemblyHasApiType(asm);
        }

        public static Type ResolveApiType(Assembly asm)
        {
            return NurApiReflection.ResolveApiType(asm);
        }

        private static bool HasNurApiType(Assembly asm)
        {
            return HasNurApiTypePublic(asm);
        }

        public static string FindPathFromFileList(ArrayList installedFiles)
        {
            if (installedFiles == null) return "";
            string best = "";
            for (int i = 0; i < installedFiles.Count; i++)
            {
                FileEntry fe = (FileEntry)installedFiles[i];
                if (fe == null || fe.Path == null) continue;
                string low = fe.Path.ToLower();
                if (low.IndexOf(".dll") < 0) continue;
                if (low.IndexOf("nur") < 0) continue;
                if (low.IndexOf("nurser") >= 0) continue;
                if (low.IndexOf("nurapi") >= 0) return fe.Path;
                if (best.Length == 0) best = fe.Path;
            }
            return best;
        }
    }
}
