using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MerlinHandheld
{
    /// <summary>
    /// Optional Nordic NUR API integration via reflection (no compile-time SDK reference).
    /// When NurApi.dll is not on the device, status reports wedge/F-key fallback only.
    /// </summary>
    public sealed class NurApiBridge : IDisposable
    {
        private readonly Form _owner;
        private readonly AppConfig _cfg;
        private object _api;
        private bool _inventoryRunning;
        private System.Windows.Forms.Timer _statusTimer;
        private string _status = "NUR: not loaded";
        private long _lastEmitTicks;

        public NurApiBridge(Form owner, AppConfig cfg)
        {
            _owner = owner;
            _cfg = cfg;
        }

        public string Status
        {
            get { return _status; }
        }

        public bool IsAvailable
        {
            get { return _api != null; }
        }

        public event EventHandler<NurTagsEventArgs> TagsInventoryReady;

        public void Start()
        {
            if (_api != null) return;
            if (!_cfg.HardwareNurEnabled) {
                _status = "NUR: disabled in config";
                return;
            }

            Assembly asm = TryLoadNurAssembly();
            if (asm == null)
            {
                _status = "NUR: DLL not found (wedge/F-keys)";
                return;
            }

            try
            {
                Type apiType = NurApiReflection.ResolveApiType(asm);
                if (apiType == null)
                {
                    _status = "NUR: type missing";
                    return;
                }

                _api = CreateNurInstance(apiType, _owner);
                HookInventoryCallback(apiType);
                if (NurApiReflection.HasMethodNamed(apiType, "ConnectIntegratedReader"))
                {
                    TryInvoke(_api, "ConnectIntegratedReader", null);
                }
                else
                {
                    TryInvoke(_api, "Connect", null);
                }
                _status = "NUR: ready";
            }
            catch (Exception ex)
            {
                _status = "NUR: " + ex.Message;
                _api = null;
            }
        }

        public void Stop()
        {
            StopInventory();
            if (_api != null)
            {
                TryInvoke(_api, "Disconnect", null);
                _api = null;
            }
            _status = "NUR: stopped";
        }

        public void TriggerInventory()
        {
            if (_api == null)
            {
                return;
            }
            if (_inventoryRunning)
            {
                StopInventory();
                EmitInventoryTags(true);
                return;
            }
            Type t = _api.GetType();
            if (NurApiReflection.HasMethodNamed(t, "StartInventoryStream"))
            {
                TryInvoke(_api, "StartInventoryStream", null);
                _inventoryRunning = true;
                SetPollTimer(true);
                return;
            }
            TryInvoke(_api, "StartInventory", null);
            _inventoryRunning = true;
            SetPollTimer(true);
        }

        public void StopInventory()
        {
            if (_api == null || !_inventoryRunning) return;
            Type t = _api.GetType();
            if (NurApiReflection.HasMethodNamed(t, "StopInventoryStream"))
            {
                TryInvoke(_api, "StopInventoryStream", null);
            }
            else
            {
                TryInvoke(_api, "StopInventory", null);
            }
            _inventoryRunning = false;
            SetPollTimer(false);
        }

        private void SetPollTimer(bool enabled)
        {
            if (_statusTimer == null) return;
            _statusTimer.Enabled = enabled;
        }

        private void HookInventoryCallback(Type apiType)
        {
            // Nordic samples expose InventoryStreamEvent / TagEvent — best-effort reflection.
            EventInfo ev = apiType.GetEvent("InventoryStreamEvent");
            if (ev == null) ev = apiType.GetEvent("TagEvent");
            if (ev == null) return;

            try
            {
                Delegate handler = Delegate.CreateDelegate(
                    ev.EventHandlerType,
                    this,
                    GetType().GetMethod("OnNurTagEvent", BindingFlags.Instance | BindingFlags.NonPublic));
                ev.AddEventHandler(_api, handler);
            }
            catch
            {
                // Polling fallback when events cannot bind (SDK version mismatch).
                if (_statusTimer == null)
                {
                    _statusTimer = new System.Windows.Forms.Timer();
                    _statusTimer.Interval = 600;
                    _statusTimer.Tick += delegate
                    {
                        if (_inventoryRunning) EmitInventoryTags(false);
                    };
                }
            }
        }

        private void OnNurTagEvent(object sender, EventArgs e)
        {
            if (_inventoryRunning) EmitInventoryTags(false);
        }

        private void EmitInventoryTags(bool force)
        {
            if (_api == null) return;
            long now = DateTime.UtcNow.Ticks;
            if (!force)
            {
                long minTicks = 800 * 10000L;
                if (now - _lastEmitTicks < minTicks)
                {
                    DiagnosticLog.LogNur("emit throttled (800ms)");
                    return;
                }
            }
            _lastEmitTicks = now;

            TryFetchTagsWithMeta(_api);
            object tags = TryInvokeReturn(_api, "GetTagStorage", null);
            if (tags == null)
            {
                DiagnosticLog.LogNur("GetTagStorage returned null");
                return;
            }
            string text = FormatTagsFromStorage(tags);
            int lineCount = text.Length == 0 ? 0 : text.Split(',').Length;
            DiagnosticLog.LogNur("emit len=" + text.Length + " approx_tags=" + lineCount + " force=" + force);
            if (text.Length > 0 && TagsInventoryReady != null)
            {
                TagsInventoryReady(this, new NurTagsEventArgs(text, force));
            }
        }

        private static string FormatTagsFromStorage(object tagStorage)
        {
            var lines = new ArrayList();
            try
            {
                IEnumerable enumerable = tagStorage as IEnumerable;
                if (enumerable == null) return "";
                foreach (object tag in enumerable)
                {
                    if (tag == null) continue;
                    string epc = ExtractEpcFromTag(tag);
                    if (epc.Length == 0) continue;
                    int rssi = ExtractRssiFromTag(tag);
                    if (rssi > -999)
                    {
                        lines.Add(epc + "|" + rssi);
                    }
                    else
                    {
                        lines.Add(epc);
                    }
                }
            }
            catch { }
            return JoinLines(lines);
        }

        private static string ExtractEpcFromTag(object tag)
        {
            if (tag == null) return "";
            Type t = tag.GetType();
            MethodInfo gm = NurApiReflection.ResolveInstanceMethod(t, "GetEpcString", null);
            if (gm != null)
            {
                object v = gm.Invoke(tag, null);
                if (v != null) return v.ToString().Trim();
            }
            object val = GetProp(t, tag, "EpcString");
            if (val == null) val = GetProp(t, tag, "EPC");
            if (val == null) val = GetProp(t, tag, "epc");
            return val == null ? "" : val.ToString().Trim();
        }

        private static void TryFetchTagsWithMeta(object api)
        {
            if (api == null) return;
            if (TryInvoke(api, "FetchTags", new object[] { true })) return;
            Type t = api.GetType();
            MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != "FetchTags") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps == null || ps.Length == 0 || ps[0].ParameterType != typeof(bool)) continue;
                if (ps.Length == 1)
                {
                    TryInvoke(api, m, new object[] { true });
                    return;
                }
            }
        }

        private static int ExtractRssiFromTag(object tag)
        {
            if (tag == null) return -999;
            Type t = tag.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            string[] names = new string[] { "RSSI", "Rssi", "rssi", "IrRssi", "ScaledRssi", "scaledRssi" };
            for (int n = 0; n < names.Length; n++)
            {
                object v = GetProp(t, tag, names[n], flags);
                if (v == null) continue;
                try
                {
                    int r = Convert.ToInt32(v);
                    if (names[n].IndexOf("Scaled") >= 0 || names[n].IndexOf("scaled") >= 0)
                    {
                        if (r >= 0 && r <= 100) return -90 + (r * 55) / 100;
                        continue;
                    }
                    return r;
                }
                catch { }
            }
            return -999;
        }

        private static object GetProp(Type t, object o, string name)
        {
            return GetProp(t, o, name, BindingFlags.Public | BindingFlags.Instance);
        }

        private static object GetProp(Type t, object o, string name, BindingFlags flags)
        {
            PropertyInfo p = t.GetProperty(name, flags);
            if (p == null) return null;
            return p.GetValue(o, null);
        }

        private static string JoinLines(ArrayList lines)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append((string)lines[i]);
            }
            return sb.ToString();
        }

        private static object CreateNurInstance(Type apiType, Form owner)
        {
            ConstructorInfo[] ctors = apiType.GetConstructors();
            if (ctors != null)
            {
                for (int i = 0; i < ctors.Length; i++)
                {
                    ParameterInfo[] ps = ctors[i].GetParameters();
                    if (ps == null || ps.Length != 1) continue;
                    if (!typeof(Control).IsAssignableFrom(ps[0].ParameterType)) continue;
                    try
                    {
                        return ctors[i].Invoke(new object[] { owner });
                    }
                    catch { }
                }
            }
            return Activator.CreateInstance(apiType);
        }

        private Assembly TryLoadNurAssembly()
        {
            string custom = _cfg.NurAssemblyPath;
            if (custom != null && custom.Length > 0)
            {
                Assembly asm = TryLoadNurFile(custom);
                if (asm != null) return asm;
            }

            string[] bootstrap = new string[]
            {
                @"\Windows\NurApiDotNetWCE.dll",
                Path.Combine(AppConfig.ConfigDirectory, "NurApiDotNetWCE.dll"),
                @"\Program Files\MerlinInventory\NurApiDotNetWCE.dll",
                @"\Windows\NurApiDotNet.dll",
            };
            for (int i = 0; i < bootstrap.Length; i++)
            {
                Assembly asm = TryLoadNurFile(bootstrap[i]);
                if (asm != null) return asm;
            }

            string[] names = { "NurApiDotNet", "NurApi", "NordicId.NurApi" };
            string[] dirs = {
                @"\Program Files\Nordic ID\NUR API",
                @"\Program Files\NordicId\NurApi",
                @"\Program Files\NID RFID Demo",
                @"\Flash\Nordic",
                Path.GetDirectoryName(AppConfig.ConfigDirectory)
            };

            for (int d = 0; d < dirs.Length; d++)
            {
                string dir = dirs[d];
                if (dir == null || !Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir, "*.dll");
                for (int i = 0; i < files.Length; i++)
                {
                    if (!NurApiReflection.IsManagedNurDllFile(Path.GetFileName(files[i]))) continue;
                    Assembly asm = TryLoadNurFile(files[i]);
                    if (asm != null) return asm;
                }
            }

            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Assembly asm = Assembly.Load(names[i]);
                    if (asm != null && NurApiReflection.AssemblyHasApiType(asm)) return asm;
                }
                catch { }
            }
            return null;
        }

        private static Assembly TryLoadNurFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                if (!NurApiReflection.IsManagedNurDllFile(Path.GetFileName(path))) return null;
                Assembly asm = Assembly.LoadFrom(path);
                if (NurApiReflection.AssemblyHasApiType(asm)) return asm;
            }
            catch { }
            return null;
        }

        private static bool HasNurApiType(Assembly asm)
        {
            return NurApiReflection.AssemblyHasApiType(asm);
        }

        private static void TryInvoke(object target, string method, object[] args)
        {
            if (target == null) return;
            MethodInfo m = NurApiReflection.ResolveInstanceMethod(target.GetType(), method, args);
            if (m != null) m.Invoke(target, args);
        }

        private static object TryInvokeReturn(object target, string method, object[] args)
        {
            if (target == null) return null;
            MethodInfo m = NurApiReflection.ResolveInstanceMethod(target.GetType(), method, args);
            if (m == null) return null;
            return m.Invoke(target, args);
        }

        public void Dispose()
        {
            if (_statusTimer != null)
            {
                _statusTimer.Enabled = false;
                _statusTimer.Dispose();
                _statusTimer = null;
            }
            Stop();
        }
    }

    public sealed class NurTagsEventArgs : EventArgs
    {
        public readonly string WedgeText;
        public readonly bool IsComplete;

        public NurTagsEventArgs(string wedgeText, bool isComplete)
        {
            WedgeText = wedgeText ?? "";
            IsComplete = isComplete;
        }
    }
}
