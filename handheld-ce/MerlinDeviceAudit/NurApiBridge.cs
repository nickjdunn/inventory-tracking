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
        private bool _connected;
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
            get { return _api != null && _connected; }
        }

        public event EventHandler<NurTagsEventArgs> TagsInventoryReady;
        public event EventHandler<NurTagReadingsEventArgs> TagReadingsReady;

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
                _connected = EstablishTransport(_api);

                _status = _connected ? "NUR: ready" : "NUR: not connected";
            }
            catch (Exception ex)
            {
                _status = "NUR: " + ex.Message;
                _api = null;
                _connected = false;
            }
        }

        /// <summary>Start inventory stream once; hardware trigger delivers tags via event.</summary>
        public void EnsureInventoryStream()
        {
            if (_api == null || !_connected || _streamRunning) return;

            Type t = _api.GetType();
            if (NurApiReflection.HasMethodNamed(t, "StartInventoryStream"))
            {
                if (TryInvokeSafe(_api, "StartInventoryStream", null))
                {
                    _streamRunning = true;
                }
                return;
            }

            if (NurApiReflection.HasMethodNamed(t, "StartInventory"))
            {
                if (TryInvokeSafe(_api, "StartInventory", null))
                {
                    _streamRunning = true;
                }
            }
        }

        public void StopInventoryStreamSafe()
        {
            if (_api == null || !_streamRunning) return;
            _streamRunning = false;

            Type t = _api.GetType();
            if (NurApiReflection.HasMethodNamed(t, "StopInventoryStream"))
            {
                TryInvokeSafe(_api, "StopInventoryStream", null);
            }
            else if (NurApiReflection.HasMethodNamed(t, "StopInventory"))
            {
                TryInvokeSafe(_api, "StopInventory", null);
            }
        }

        /// <summary>Manual F1 toggle: stop stream and emit collected tags.</summary>
        public void TriggerInventory()
        {
            if (_api == null || !_connected) return;

            if (_streamRunning)
            {
                StopInventoryStreamSafe();
                EmitTags(true);
                return;
            }

            EnsureInventoryStream();
        }

        private void OnInventoryStream(object sender, EventArgs e)
        {
            if (_owner.InvokeRequired)
            {
                _owner.BeginInvoke(new EventHandler(delegate { OnInventoryStream(sender, e); }), null, EventArgs.Empty);
                return;
            }

            if (IsStreamStoppedNotification(e))
            {
                _streamRunning = false;
                EnsureInventoryStream();
            }

            EmitTags(false);
        }

        private static bool IsStreamStoppedNotification(EventArgs e)
        {
            if (e == null) return false;
            try
            {
                Type t = e.GetType();
                bool stopped;
                if (NurApiReflection.TryGetBoolProperty(e, "stopped", out stopped)) return stopped;
                if (NurApiReflection.TryGetBoolProperty(e, "Stopped", out stopped)) return stopped;
                object data = GetProp(t, e, "data");
                if (data != null && NurApiReflection.TryGetBoolProperty(data, "stopped", out stopped))
                {
                    return stopped;
                }
            }
            catch { }
            return false;
        }

        private void EmitTags(bool force)
        {
            if (_api == null || !_connected) return;
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

            NurTagReading[] readings = CollectReadings(tags);
            string text = JoinEpcs(readings);
            if (readings.Length > 0 && TagReadingsReady != null)
            {
                TagReadingsReady(this, new NurTagReadingsEventArgs(readings, text));
            }
            if (text.Length > 0 && TagsInventoryReady != null)
            {
                TagsInventoryReady(this, new NurTagsEventArgs(text, force));
            }
        }

        public NurTagReading[] ReadTagsNow()
        {
            if (_api == null || !_connected) return new NurTagReading[0];
            object tags = TryInvokeReturn(_api, "GetTagStorage", null);
            if (tags == null) tags = TryInvokeReturn(_api, "FetchTags", null);
            if (tags == null) return new NurTagReading[0];
            return CollectReadings(tags);
        }

        private static NurTagReading[] CollectReadings(object tagStorage)
        {
            var list = new ArrayList();
            try
            {
                IEnumerable enumerable = tagStorage as IEnumerable;
                if (enumerable == null) return new NurTagReading[0];
                long ts = DateTime.UtcNow.Ticks / 10000L;
                foreach (object tag in enumerable)
                {
                    if (tag == null) continue;
                    string epc = ExtractEpcFromTag(tag);
                    if (epc.Length == 0) continue;
                    bool hasRssi;
                    int rssi = ExtractRssiFromTag(tag, out hasRssi);
                    var r = new NurTagReading();
                    r.Epc = epc;
                    r.Rssi = rssi;
                    r.HasRssi = hasRssi;
                    r.TimestampUtc = ts;
                    list.Add(r);
                }
            }
            catch { }
            var arr = new NurTagReading[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = (NurTagReading)list[i];
            return arr;
        }

        private static string JoinEpcs(NurTagReading[] readings)
        {
            if (readings == null || readings.Length == 0) return "";
            var lines = new ArrayList();
            for (int i = 0; i < readings.Length; i++)
            {
                if (readings[i].Epc.Length > 0) lines.Add(readings[i].Epc);
            }
            return JoinLines(lines);
        }

        private static int ExtractRssiFromTag(object tag, out bool hasRssi)
        {
            hasRssi = false;
            if (tag == null) return 0;
            try
            {
                Type t = tag.GetType();
                string[] names = new string[]
                {
                    "Rssi", "RSSI", "rssi", "m_Rssi", "m_RSSI",
                    "SignalStrength", "ReadStrength", "Strength",
                };
                for (int n = 0; n < names.Length; n++)
                {
                    object v = GetProp(t, tag, names[n]);
                    int r;
                    if (TryParseRssi(v, out hasRssi, out r)) return r;
                }

                PropertyInfo[] props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < props.Length; i++)
                {
                    string pn = props[i].Name.ToLower();
                    if (pn.IndexOf("rssi") < 0 && pn.IndexOf("signal") < 0 && pn.IndexOf("strength") < 0)
                    {
                        continue;
                    }
                    object v = props[i].GetValue(tag, null);
                    int r;
                    if (TryParseRssi(v, out hasRssi, out r)) return r;
                }

                string[] methods = new string[] { "GetRssi", "GetRSS", "GetSignalStrength" };
                for (int m = 0; m < methods.Length; m++)
                {
                    MethodInfo mi = NurApiReflection.ResolveInstanceMethod(t, methods[m], null);
                    if (mi == null) continue;
                    object v = mi.Invoke(tag, null);
                    int r;
                    if (TryParseRssi(v, out hasRssi, out r)) return r;
                }
            }
            catch { }
            return 0;
        }

        private static bool TryParseRssi(object v, out bool has, out int rssi)
        {
            has = false;
            rssi = 0;
            if (v == null) return false;
            try
            {
                if (v is int) { has = true; rssi = (int)v; return true; }
                if (v is short) { has = true; rssi = (int)(short)v; return true; }
                if (v is byte) { has = true; rssi = (int)(byte)v; return true; }
                rssi = Convert.ToInt32(v);
                has = true;
                return true;
            }
            catch { }
            return false;
        }

        private static string ExtractEpcFromTag(object tag)
        {
            if (tag == null) return "";
            Type t = tag.GetType();
            MethodInfo m = NurApiReflection.ResolveInstanceMethod(t, "GetEpcString", null);
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

        private static bool EstablishTransport(object api)
        {
            if (api == null) return false;

            Type t = api.GetType();
            if (NurApiReflection.HasMethodNamed(t, "ConnectIntegratedReader"))
            {
                TryInvokeSafe(api, "ConnectIntegratedReader", null);
                if (IsTransportConnected(api)) return true;
            }

            if (TryConnectIntegratedUri(api) && IsTransportConnected(api))
            {
                return true;
            }

            TryConnectLegacy(api);
            return IsTransportConnected(api);
        }

        private static bool IsTransportConnected(object api)
        {
            bool v;
            if (NurApiReflection.TryGetBoolProperty(api, "IsConnected", out v)) return v;
            if (NurApiReflection.TryGetBoolProperty(api, "Connected", out v)) return v;
            if (NurApiReflection.TryGetBoolProperty(api, "IsTransportConnected", out v)) return v;
            // Some CE builds omit connection flags after ConnectIntegratedReader.
            return true;
        }

        private static bool TryConnectIntegratedUri(object api)
        {
            try
            {
                Uri uri = new Uri("int://integrated_reader/?name=Integrated reader");
                Type t = api.GetType();
                MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m.Name != "Connect") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps == null || ps.Length != 1) continue;
                    Type pt = ps[0].ParameterType;
                    if (pt == typeof(Uri))
                    {
                        return TryInvokeSafe(api, m, new object[] { uri });
                    }
                    if (pt == typeof(string))
                    {
                        return TryInvokeSafe(api, m, new object[] { uri.ToString() });
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryInvokeSafe(object target, MethodInfo method, object[] args)
        {
            if (target == null || method == null) return false;
            try
            {
                method.Invoke(target, args);
                return true;
            }
            catch (TargetInvocationException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeSafe(object target, string method, object[] args)
        {
            MethodInfo m = NurApiReflection.ResolveInstanceMethod(target.GetType(), method, args);
            return TryInvokeSafe(target, m, args);
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
                if (ps == null || ps.Length == 0)
                {
                    TryInvokeSafe(api, m, null);
                    return;
                }
            }
        }

        private static object TryInvokeReturn(object target, string method, object[] args)
        {
            if (target == null) return null;
            MethodInfo m = NurApiReflection.ResolveInstanceMethod(target.GetType(), method, args);
            if (m == null) return null;
            try
            {
                return m.Invoke(target, args);
            }
            catch (TargetInvocationException tie)
            {
                if (tie.InnerException != null) throw tie.InnerException;
                throw;
            }
        }

        private void HookInventoryStreamEvent(Type apiType)
        {
            EventInfo ev = apiType.GetEvent("InventoryStreamEvent");
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

        public void Dispose()
        {
            StopInventoryStreamSafe();
            if (_api != null)
            {
                TryInvokeSafe(_api, "Dispose", null);
                _api = null;
            }
            _connected = false;
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
