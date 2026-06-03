using System;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace MerlinAudit
{
    /// <summary>
    /// Nordic NurApiDotNet on Windows CE (ConnectIntegratedReader, InventoryStream).
    /// </summary>
    public sealed class NurApiBridge : IDisposable
    {
        private readonly Form _owner;
        private readonly AuditConfig _cfg;
        private object _api;
        private bool _streamRunning;
        private string _status = "NUR: not loaded";
        private long _lastEmitTicks;

        public NurApiBridge(Form owner, AuditConfig cfg)
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
            if (!_cfg.HardwareNurEnabled)
            {
                _status = "NUR: disabled";
                return;
            }

            Assembly asm = NurAssemblyLocator.LoadNurAssembly(_cfg);
            if (asm == null)
            {
                NurDllDiscoveryResult fetch = new NurDllDiscoveryResult();
                NurDllDiscovery.TryFetchFromServer(_cfg, fetch);
                asm = NurAssemblyLocator.LoadNurAssembly(_cfg);
            }
            if (asm == null)
            {
                _status = "NUR: DLL not found";
                if (NurAssemblyLocator.LastSummary != null && NurAssemblyLocator.LastSummary.Length > 0)
                {
                    _status += " (" + NurAssemblyLocator.LastSummary + ")";
                }
                return;
            }

            try
            {
                Type apiType = NurAssemblyLocator.ResolveApiType(asm);
                if (apiType == null)
                {
                    _status = "NUR: type missing";
                    string hint = ListNurTypeHints(asm);
                    if (hint.Length > 0) _status += " [" + hint + "]";
                    return;
                }

                _api = CreateNurInstance(apiType, _owner);
                if (_api == null)
                {
                    _status = "NUR: create failed";
                    return;
                }

                HookInventoryStreamEvent(apiType);

                if (NurApiReflection.HasMethodNamed(apiType, "ConnectIntegratedReader"))
                {
                    TryInvoke(_api, "ConnectIntegratedReader", null);
                }
                else
                {
                    TryConnectLegacy(_api);
                }

                _status = "NUR: ready";
            }
            catch (Exception ex)
            {
                _status = "NUR: " + ex.Message;
                _api = null;
            }
        }

        public void TriggerInventory()
        {
            if (_api == null) return;

            if (_streamRunning)
            {
                StopStream();
                EmitTags(true);
                return;
            }

            Type t = _api.GetType();
            if (NurApiReflection.HasMethodNamed(t, "StartInventoryStream"))
            {
                TryInvoke(_api, "StartInventoryStream", null);
                _streamRunning = true;
                return;
            }

            if (NurApiReflection.HasMethodNamed(t, "Inventory"))
            {
                TryInvoke(_api, "ClearTags", null);
                object ir = TryInvokeReturn(_api, "Inventory", null);
                EmitTags(true);
                return;
            }

            TryInvoke(_api, "StartInventory", null);
            _streamRunning = true;
        }

        public void StopStream()
        {
            if (_api == null || !_streamRunning) return;
            Type t = _api.GetType();
            if (NurApiReflection.HasMethodNamed(t, "StopInventoryStream"))
            {
                TryInvoke(_api, "StopInventoryStream", null);
            }
            else
            {
                TryInvoke(_api, "StopInventory", null);
            }
            _streamRunning = false;
        }

        private void HookInventoryStreamEvent(Type apiType)
        {
            EventInfo ev = apiType.GetEvent("InventoryStreamEvent");
            if (ev == null) ev = apiType.GetEvent("InventoryStreamEvent");
            if (ev == null) return;

            try
            {
                Delegate handler = Delegate.CreateDelegate(
                    ev.EventHandlerType,
                    this,
                    GetType().GetMethod("OnInventoryStream", BindingFlags.Instance | BindingFlags.NonPublic));
                ev.AddEventHandler(_api, handler);
            }
            catch { }
        }

        private void OnInventoryStream(object sender, EventArgs e)
        {
            if (_owner.InvokeRequired)
            {
                _owner.BeginInvoke(new EventHandler(delegate { OnInventoryStream(sender, e); }), null, EventArgs.Empty);
                return;
            }
            EmitTags(false);
        }

        private void EmitTags(bool force)
        {
            if (_api == null) return;
            long now = DateTime.UtcNow.Ticks;
            if (!force)
            {
                long minTicks = 600 * 10000L;
                if (now - _lastEmitTicks < minTicks) return;
            }
            _lastEmitTicks = now;

            object tags = TryInvokeReturn(_api, "GetTagStorage", null);
            if (tags == null)
            {
                tags = TryInvokeReturn(_api, "FetchTags", null);
            }
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
                    lines.Add(epc);
                }
            }
            catch { }
            return JoinLines(lines);
        }

        private static string ExtractEpcFromTag(object tag)
        {
            if (tag == null) return "";
            Type t = tag.GetType();
            MethodInfo m = t.GetMethod("GetEpcString");
            if (m != null)
            {
                object v = m.Invoke(tag, null);
                if (v != null) return v.ToString().Trim();
            }
            object p = GetProp(t, tag, "EpcString");
            if (p == null) p = GetProp(t, tag, "EPC");
            return p == null ? "" : p.ToString().Trim();
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

        private static string ListNurTypeHints(Assembly asm)
        {
            if (asm == null) return "";
            try
            {
                Type[] types = asm.GetTypes();
                var sb = new StringBuilder();
                int n = 0;
                for (int i = 0; i < types.Length && n < 3; i++)
                {
                    if (types[i].Name != "NurApi") continue;
                    if (sb.Length > 0) sb.Append(",");
                    sb.Append(types[i].FullName);
                    n++;
                }
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static void TryConnectLegacy(object api)
        {
            Type t = api.GetType();
            MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != "Connect" && m.Name != "Open") continue;
                ParameterInfo[] ps = m.GetParameters();
                try
                {
                    if (ps == null || ps.Length == 0)
                    {
                        m.Invoke(api, null);
                        return;
                    }
                }
                catch { }
            }
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
            StopStream();
            if (_api != null)
            {
                TryInvoke(_api, "Dispose", null);
                _api = null;
            }
            _status = "NUR: stopped";
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
