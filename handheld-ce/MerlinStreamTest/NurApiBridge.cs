using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MerlinStream
{
    /// <summary>
    /// Optional Nordic NUR API integration via reflection (no compile-time SDK reference).
    /// When NurApi.dll is not on the device, status reports wedge/F-key fallback only.
    /// </summary>
    public sealed class NurApiBridge : IDisposable
    {
        private readonly StreamConfig _cfg;
        private object _api;
        private bool _inventoryRunning;
        private System.Windows.Forms.Timer _statusTimer;
        private string _status = "NUR: not loaded";
        private long _lastEmitTicks;

        public NurApiBridge(StreamConfig cfg)
        {
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
                Type apiType = asm.GetType("NurApi.NurApi", false);
                if (apiType == null) apiType = asm.GetType("NurApi", false);
                if (apiType == null)
                {
                    _status = "NUR: NurApi type missing";
                    return;
                }

                _api = Activator.CreateInstance(apiType);
                TryInvoke(_api, "Connect", null);
                HookInventoryCallback(apiType);
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
            TryInvoke(_api, "StartInventory", null);
            _inventoryRunning = true;
            SetPollTimer(true);
        }

        public void StopInventory()
        {
            if (_api == null || !_inventoryRunning) return;
            TryInvoke(_api, "StopInventory", null);
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
                if (now - _lastEmitTicks < minTicks) return;
            }
            _lastEmitTicks = now;

            object tags = TryInvokeReturn(_api, "GetTagStorage", null);
            if (tags == null) return;
            string text = FormatTagsFromStorage(tags);
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
            object v = GetProp(t, tag, "EpcString");
            if (v == null) v = GetProp(t, tag, "EPC");
            if (v == null) v = GetProp(t, tag, "epc");
            return v == null ? "" : v.ToString().Trim();
        }

        private static int ExtractRssiFromTag(object tag)
        {
            if (tag == null) return -999;
            Type t = tag.GetType();
            object v = GetProp(t, tag, "RSSI");
            if (v == null) v = GetProp(t, tag, "Rssi");
            if (v == null) return -999;
            try
            {
                return Convert.ToInt32(v);
            }
            catch
            {
                return -999;
            }
        }

        private static object GetProp(Type t, object o, string name)
        {
            PropertyInfo p = t.GetProperty(name);
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

        private Assembly TryLoadNurAssembly()
        {
            string[] names = { "NurApiDotNet", "NurApi", "NordicId.NurApi" };
            string custom = _cfg.NurAssemblyPath;
            if (custom != null && custom.Length > 0)
            {
                try
                {
                    if (File.Exists(custom)) return Assembly.LoadFrom(custom);
                }
                catch { }
            }

            string[] dirs = {
                @"\Program Files\Nordic ID\NUR API",
                @"\Program Files\NordicId\NurApi",
                @"\Flash\Nordic",
                Path.GetDirectoryName(StreamConfig.ConfigDirectory)
            };

            for (int d = 0; d < dirs.Length; d++)
            {
                string dir = dirs[d];
                if (dir == null || !Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir, "*.dll");
                for (int i = 0; i < files.Length; i++)
                {
                    string low = files[i].ToLower();
                    if (low.IndexOf("nur") < 0) continue;
                    try
                    {
                        return Assembly.LoadFrom(files[i]);
                    }
                    catch { }
                }
            }

            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    return Assembly.Load(names[i]);
                }
                catch { }
            }
            return null;
        }

        private static void TryInvoke(object target, string method, object[] args)
        {
            if (target == null) return;
            MethodInfo m = target.GetType().GetMethod(method);
            if (m != null) m.Invoke(target, args);
        }

        private static object TryInvokeReturn(object target, string method, object[] args)
        {
            if (target == null) return null;
            MethodInfo m = target.GetType().GetMethod(method);
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
