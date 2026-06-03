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
        private NurTagReading[] _cachedReadings = new NurTagReading[0];
        private long _cacheTicks;
        private System.Windows.Forms.Timer _streamRestartTimer;
        private bool _streamRestartPending;
        private bool _streamInvokeScheduled;
        private bool _streamInvokePending;
        private EventArgs _pendingStreamEvent;
        private int _inventoryPulseCount;
        private const long StreamEmitMinTicks = 25 * 10000L;
        private int _pollMetaCounter;
        private readonly object _apiLock = new object();
        private readonly Hashtable _liveRssiByEpc = new Hashtable();
        private readonly Hashtable _liveRssiTicks = new Hashtable();
        private const long LiveRssiMaxAgeTicks = 500 * 10000L;

        /// <summary>RSSI track form: no BeginInvoke per trigger pulse (timer polls instead).</summary>
        public bool PollOnlyMode;

        public bool IsStreamRunning
        {
            get { return _streamRunning; }
        }

        public long LastEmitTicks
        {
            get { return _lastEmitTicks; }
        }

        public NurProfileStatus ReaderProfile = new NurProfileStatus();

        public string ReaderProfileLine
        {
            get { return ReaderProfile.ToDisplayLine(); }
        }

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

                if (_connected)
                {
                    NurRfPreset preset = NurRfPresets.Get(_cfg.RfPresetIndex);
                    ReaderProfile = NurReaderProfile.ApplyRfSettings(
                        _api, preset.LinkFreqHz, preset.TxLevel);
                    ReaderProfile.EpcSelectMethodFound =
                        NurReaderProfile.HasInventorySelectByEpc(_api);
                    _status = "NUR: ready";
                }
                else
                {
                    _status = "NUR: not connected";
                }
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
        /// <summary>Hardware trigger / F1: clear storage, run a read round, raise tag event.</summary>
        /// <summary>Hardware trigger — do not clear tag storage (hold-to-scan accumulates tags).</summary>
        public void TriggerInventory()
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return;
                if (!_streamRunning)
                {
                    EnsureInventoryStream();
                }
                if (!(_streamRunning && PollOnlyMode))
                {
                    TryRunInventoryPulse();
                }
                InvalidateTagCache();
                EmitTags(true);
            }
        }

        /// <summary>Clear tag storage, pulse once — track mode sees only tags in current sweep.</summary>
        public void TriggerFreshRound()
        {
            TriggerFreshRound(null);
        }

        /// <summary>Track round: EPC-select inventory when possible, else fresh clear+pulse.</summary>
        public void TriggerFreshRound(string targetEpc)
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return;
                if (!_streamRunning)
                {
                    EnsureInventoryStream();
                }

                TryClearTagStorageInstance();
                bool selected = false;
                if (targetEpc != null && targetEpc.Length > 0)
                {
                    selected = NurReaderProfile.TryInventorySelectByEpc(_api, targetEpc);
                    ReaderProfile.LastEpcSelectOk = selected;
                }
                if (!selected)
                {
                    if (!(_streamRunning && PollOnlyMode))
                    {
                        TryRunInventoryPulse();
                    }
                }

                InvalidateTagCache();
                if (targetEpc != null && targetEpc.Length > 0)
                {
                    EmitTrackTags(targetEpc);
                }
                else
                {
                    EmitTags(true);
                }
            }
        }

        /// <summary>Current inventory round only — exact EPC match (no storage/cache).</summary>
        public NurTagReading[] FetchTrackRoundTags(string targetEpc)
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected || targetEpc == null || targetEpc.Length == 0)
                {
                    return new NurTagReading[0];
                }
                TryFetchTagsWithMeta(_api);
                NurTagReading[] round = BuildCurrentRoundReadings(null);
                return NurReaderProfile.FilterExactEpc(round, targetEpc);
            }
        }

        /// <summary>All tags in the current inventory round (for bench noise count).</summary>
        public NurTagReading[] FetchRoundAllTags()
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return new NurTagReading[0];
                TryFetchTagsWithMeta(_api);
                return BuildCurrentRoundReadings(null);
            }
        }

        public NurProfileStatus ApplyRfPreset(NurRfPreset preset)
        {
            if (preset == null || _api == null || !_connected)
            {
                return ReaderProfile ?? new NurProfileStatus();
            }
            ReaderProfile = NurReaderProfile.ApplyRfSettings(
                _api, preset.LinkFreqHz, preset.TxLevel);
            ReaderProfile.EpcSelectMethodFound = NurReaderProfile.HasInventorySelectByEpc(_api);
            return ReaderProfile;
        }

        /// <summary>Bench pulse: clear storage, optional EPC select, one inventory round.</summary>
        public void BenchInventoryPulse(string targetEpc, bool useEpcSelect)
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return;
                if (!_streamRunning) EnsureInventoryStream();
                TryClearTagStorageInstance();
                bool selected = false;
                if (useEpcSelect && targetEpc != null && targetEpc.Length > 0)
                {
                    selected = NurReaderProfile.TryInventorySelectByEpc(_api, targetEpc);
                }
                if (!selected && !(_streamRunning && PollOnlyMode))
                {
                    TryRunInventoryPulse();
                }
                InvalidateTagCache();
            }
        }

        private void EmitTrackTags(string targetEpc)
        {
            if (_api == null || !_connected) return;
            long now = DateTime.UtcNow.Ticks;
            _lastEmitTicks = now;

            NurTagReading[] readings = FetchTrackRoundTags(targetEpc);
            _cachedReadings = readings;
            _cacheTicks = now;

            if (TagReadingsReady != null)
            {
                TagReadingsReady(this, new NurTagReadingsEventArgs(readings, JoinEpcs(readings)));
            }
        }

        /// <summary>On-screen refresh: must run on UI thread (CE NUR requirement).</summary>
        public NurTagReading[] ForceFetchTags()
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return new NurTagReading[0];
                InvalidateTagCache();
                _liveRssiByEpc.Clear();
                _liveRssiTicks.Clear();
                TryClearTagStorageInstance();
                if (!_streamRunning)
                {
                    EnsureInventoryStream();
                }
                TryRunInventoryPulse();
                TryFetchTagsWithMeta(_api);
                NurTagReading[] readings = ReadTagsFromApiUncached();
                _cachedReadings = readings;
                _cacheTicks = DateTime.UtcNow.Ticks;
                EnsureInventoryStream();
                return readings;
            }
        }

        private void InvalidateTagCache()
        {
            _cacheTicks = 0;
            _cachedReadings = new NurTagReading[0];
        }

        private void TryClearTagStorageInstance()
        {
            TryInvokeSafe(_api, "ClearTags", null);
        }

        private void TryRunInventoryPulse()
        {
            if (_api == null) return;
            string[] names = new string[] { "Inventory", "RunInventory", "StartInventory" };
            for (int i = 0; i < names.Length; i++)
            {
                if (TryInvokeSafe(_api, names[i], null)) return;
            }
        }

        private void OnInventoryStream(object sender, EventArgs e)
        {
            if (PollOnlyMode)
            {
                if (IsStreamStoppedNotification(e))
                {
                    _streamRunning = false;
                    ScheduleStreamRestart();
                }
                else
                {
                    _inventoryPulseCount++;
                    if (_inventoryPulseCount > 16) _inventoryPulseCount = 16;
                }
                return;
            }

            if (_owner.InvokeRequired)
            {
                _pendingStreamEvent = e;
                _streamInvokePending = true;
                if (_streamInvokeScheduled) return;
                _streamInvokeScheduled = true;
                _owner.BeginInvoke(new EventHandler(delegate
                {
                    _streamInvokeScheduled = false;
                    do
                    {
                        _streamInvokePending = false;
                        OnInventoryStream(sender, _pendingStreamEvent);
                    }
                    while (_streamInvokePending);
                }), null, EventArgs.Empty);
                return;
            }

            if (IsStreamStoppedNotification(e))
            {
                _streamRunning = false;
                ScheduleStreamRestart();
                return;
            }

            EmitTags(false, e);
        }

        private void EmitTags(bool force)
        {
            EmitTags(force, null);
        }

        private void EmitTags(bool force, EventArgs streamEvent)
        {
            if (_api == null || !_connected) return;
            long now = DateTime.UtcNow.Ticks;
            if (!force)
            {
                if (now - _lastEmitTicks < StreamEmitMinTicks) return;
            }
            _lastEmitTicks = now;

            NurTagReading[] readings;
            lock (_apiLock)
            {
                readings = ReadTagsFromApiUncached(streamEvent);
                _cachedReadings = readings;
                _cacheTicks = now;
            }

            if (readings.Length == 0)
            {
                if (force) TagReadingsReadyEmpty();
                return;
            }
            string text = JoinEpcs(readings);
            if (TagReadingsReady != null)
            {
                TagReadingsReady(this, new NurTagReadingsEventArgs(readings, text));
            }
            if (text.Length > 0 && TagsInventoryReady != null)
            {
                TagsInventoryReady(this, new NurTagsEventArgs(text, force));
            }
        }

        public bool ConsumeInventoryPulse()
        {
            if (_inventoryPulseCount <= 0) return false;
            _inventoryPulseCount--;
            return true;
        }

        public bool HasInventoryPulse()
        {
            return _inventoryPulseCount > 0;
        }

        /// <summary>Read tags for UI timer. With stream+PollOnlyMode, only FetchTags (never Inventory() — that breaks stream).</summary>
        public NurTagReading[] PollTagsFromReader(bool forTrack)
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return new NurTagReading[0];
                if (!_streamRunning)
                {
                    EnsureInventoryStream();
                }

                bool streamPoll = _streamRunning && PollOnlyMode;
                if (!streamPoll)
                {
                    if (forTrack)
                    {
                        TryRunInventoryPulse();
                    }
                    else
                    {
                        _pollMetaCounter++;
                        if ((_pollMetaCounter & 1) == 0)
                        {
                            TryRunInventoryPulse();
                        }
                    }
                }

                PulseInventoryRead();

                NurTagReading[] readings = ReadTagsFromApiUncached();
                if (readings.Length == 0 && (forTrack || !streamPoll))
                {
                    if (!streamPoll)
                    {
                        TryRunInventoryPulse();
                    }
                    TryFetchTagsWithMeta(_api);
                    readings = ReadTagsFromApiUncached();
                }

                _cachedReadings = readings;
                _cacheTicks = DateTime.UtcNow.Ticks;
                return readings;
            }
        }

        private void PulseInventoryRead()
        {
            if (_api == null) return;
            TryFetchTagsWithMeta(_api);
        }

        private void TagReadingsReadyEmpty()
        {
            if (TagReadingsReady == null) return;
            TagReadingsReady(this, new NurTagReadingsEventArgs(new NurTagReading[0], ""));
        }

        private void ScheduleStreamRestart()
        {
            if (_api == null || !_connected) return;
            if (_streamRestartPending) return;
            _streamRestartPending = true;
            if (_streamRestartTimer == null)
            {
                _streamRestartTimer = new System.Windows.Forms.Timer();
                _streamRestartTimer.Interval = 900;
                _streamRestartTimer.Tick += delegate
                {
                    _streamRestartTimer.Enabled = false;
                    _streamRestartPending = false;
                    EnsureInventoryStream();
                };
            }
            _streamRestartTimer.Enabled = false;
            _streamRestartTimer.Enabled = true;
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

        public NurTagReading[] ReadTagsNow()
        {
            return ReadTagsCached(false);
        }

        /// <summary>Fresh read for track polling — no cache.</summary>
        public NurTagReading[] ReadTagsLive()
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return new NurTagReading[0];
                NurTagReading[] readings = ReadTagsFromApiUncached(null);
                _cachedReadings = readings;
                _cacheTicks = DateTime.UtcNow.Ticks;
                return readings;
            }
        }

        /// <summary>Tags seen in the current inventory round only (no storage/cache overlay). For RSSI track.</summary>
        public NurTagReading[] ReadTagsCurrentRound(bool allowPulse)
        {
            return ReadTagsCurrentRound(null, allowPulse);
        }

        /// <summary>Current-round tags, optionally including tags from the stream event that just fired.</summary>
        public NurTagReading[] ReadTagsCurrentRound(EventArgs streamEvent, bool allowPulse)
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return new NurTagReading[0];
                if (!_streamRunning)
                {
                    EnsureInventoryStream();
                }

                PruneExpiredLiveCache();
                NurTagReading[] readings = BuildCurrentRoundReadings(streamEvent);
                if (readings.Length == 0 && allowPulse)
                {
                    TryRunInventoryPulse();
                    TryFetchTagsWithMeta(_api);
                    readings = BuildCurrentRoundReadings(null);
                }
                return readings;
            }
        }

        private NurTagReading[] ReadTagsCached(bool forceFetch)
        {
            lock (_apiLock)
            {
                if (_api == null || !_connected) return new NurTagReading[0];
                long now = DateTime.UtcNow.Ticks;
                long minCache = 400 * 10000L;
                if (!forceFetch && _cachedReadings != null && _cachedReadings.Length > 0
                    && (now - _cacheTicks) < minCache)
                {
                    return _cachedReadings;
                }

                NurTagReading[] readings = ReadTagsFromApiUncached();
                _cachedReadings = readings;
                _cacheTicks = now;
                return readings;
            }
        }

        private NurTagReading[] ReadTagsFromApiUncached()
        {
            return ReadTagsFromApiUncached(null);
        }

        /// <summary>Merge this-round tags, storage discovery, and live RSSI cache (not peak-only storage).</summary>
        private NurTagReading[] ReadTagsFromApiUncached(EventArgs streamEvent)
        {
            if (_api == null || !_connected) return new NurTagReading[0];
            TryFetchTagsWithMeta(_api);

            var byEpc = new Hashtable();

            NurTagReading[] fromEvent = CollectReadingsFromStreamEvent(streamEvent);
            MergeIntoByEpc(byEpc, fromEvent, true);

            NurTagReading[] fromRound = CollectTagsViaGetTagDataArray(_api);
            MergeIntoByEpc(byEpc, fromRound, true);

            object tags = TryInvokeReturn(_api, "GetTagStorage", null);
            if (tags != null)
            {
                NurTagReading[] fromStore = CollectReadings(tags);
                MergeIntoByEpc(byEpc, fromStore, false);
            }

            return HashtableToReadings(byEpc);
        }

        private NurTagReading[] BuildCurrentRoundReadings(EventArgs streamEvent)
        {
            if (_api == null || !_connected) return new NurTagReading[0];
            TryFetchTagsWithMeta(_api);

            var byEpc = new Hashtable();
            NurTagReading[] fromEvent = CollectReadingsFromStreamEvent(streamEvent);
            MergeIntoByEpc(byEpc, fromEvent, true);

            NurTagReading[] fromRound = CollectTagsViaGetTagDataArray(_api);
            MergeIntoByEpc(byEpc, fromRound, true);
            return HashtableToReadings(byEpc);
        }

        private static NurTagReading[] HashtableToReadings(Hashtable byEpc)
        {
            if (byEpc == null || byEpc.Count == 0) return new NurTagReading[0];
            var arr = new NurTagReading[byEpc.Count];
            int idx = 0;
            foreach (DictionaryEntry de in byEpc)
            {
                arr[idx++] = (NurTagReading)de.Value;
            }
            return arr;
        }

        private void PruneExpiredLiveCache()
        {
            long now = DateTime.UtcNow.Ticks;
            var stale = new ArrayList();
            foreach (DictionaryEntry de in _liveRssiTicks)
            {
                long tick = (long)de.Value;
                if ((now - tick) >= LiveRssiMaxAgeTicks)
                {
                    stale.Add(de.Key);
                }
            }
            for (int i = 0; i < stale.Count; i++)
            {
                string epc = (string)stale[i];
                _liveRssiByEpc.Remove(epc);
                _liveRssiTicks.Remove(epc);
            }
        }

        private void MergeIntoByEpc(Hashtable byEpc, NurTagReading[] batch, bool isLiveRound)
        {
            if (batch == null || batch.Length == 0) return;
            for (int i = 0; i < batch.Length; i++)
            {
                NurTagReading n = batch[i];
                if (n == null || n.Epc.Length == 0) continue;
                string epc = NormalizeEpcLocal(n.Epc);
                if (epc.Length == 0) continue;

                if (isLiveRound && n.HasRssi)
                {
                    _liveRssiByEpc[epc] = n.Rssi;
                    _liveRssiTicks[epc] = DateTime.UtcNow.Ticks;
                }

                NurTagReading existing = (NurTagReading)byEpc[epc];
                if (existing == null)
                {
                    existing = new NurTagReading();
                    existing.Epc = epc;
                    byEpc[epc] = existing;
                }

                if (isLiveRound && n.HasRssi)
                {
                    existing.Rssi = n.Rssi;
                    existing.HasRssi = true;
                    existing.TimestampUtc = n.TimestampUtc;
                }
                else
                {
                    long now = DateTime.UtcNow.Ticks;
                    object cached = _liveRssiByEpc[epc];
                    object tickObj = _liveRssiTicks[epc];
                    long cachedAt = tickObj != null ? (long)tickObj : 0;
                    if (cached != null && cachedAt > 0 && (now - cachedAt) < LiveRssiMaxAgeTicks)
                    {
                        existing.Rssi = (int)cached;
                        existing.HasRssi = true;
                    }
                    else if (n.HasRssi && !existing.HasRssi)
                    {
                        existing.Rssi = n.Rssi;
                        existing.HasRssi = true;
                        existing.TimestampUtc = n.TimestampUtc;
                    }
                }
            }
        }

        private static string NormalizeEpcLocal(string epc)
        {
            if (epc == null) return "";
            epc = epc.Trim().ToUpper();
            int comma = epc.IndexOf(',');
            if (comma > 0) epc = epc.Substring(0, comma);
            return epc;
        }

        private static NurTagReading[] CollectReadingsFromStreamEvent(EventArgs e)
        {
            if (e == null) return new NurTagReading[0];
            try
            {
                Type t = e.GetType();
                object tags = GetProp(t, e, "tags");
                if (tags == null) tags = GetProp(t, e, "Tags");
                object data = GetProp(t, e, "data");
                if (tags == null && data != null)
                {
                    Type dt = data.GetType();
                    tags = GetProp(dt, data, "tags");
                    if (tags == null) tags = GetProp(dt, data, "Tags");
                }
                if (tags == null) return new NurTagReading[0];
                if (tags is IEnumerable && !(tags is string))
                {
                    return CollectReadings(tags);
                }
            }
            catch { }
            return new NurTagReading[0];
        }

        private static NurTagReading[] MergeTagReadings(NurTagReading[] existing, NurTagReading[] incoming)
        {
            if (incoming == null || incoming.Length == 0)
            {
                return existing ?? new NurTagReading[0];
            }
            if (existing == null || existing.Length == 0)
            {
                return incoming;
            }

            var byEpc = new Hashtable();
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] == null || existing[i].Epc.Length == 0) continue;
                byEpc[existing[i].Epc] = existing[i];
            }
            for (int i = 0; i < incoming.Length; i++)
            {
                NurTagReading n = incoming[i];
                if (n == null || n.Epc.Length == 0) continue;
                NurTagReading old = (NurTagReading)byEpc[n.Epc];
                if (old == null)
                {
                    byEpc[n.Epc] = n;
                    continue;
                }
                if (n.HasRssi)
                {
                    old.Rssi = n.Rssi;
                    old.HasRssi = true;
                }
            }

            var arr = new NurTagReading[byEpc.Count];
            int idx = 0;
            foreach (DictionaryEntry de in byEpc)
            {
                arr[idx++] = (NurTagReading)de.Value;
            }
            return arr;
        }

        private static void TryFetchTagsWithMeta(object api)
        {
            if (api == null) return;
            if (TryInvokeSafe(api, "FetchTags", new object[] { true })) return;

            Type t = api.GetType();
            MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != "FetchTags") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps == null || ps.Length == 0) continue;
                if (ps[0].ParameterType != typeof(bool)) continue;

                if (ps.Length == 1)
                {
                    TryInvokeSafe(api, m, new object[] { true });
                    return;
                }

                if (ps.Length == 2)
                {
                    try
                    {
                        object[] args = new object[] { true, 0 };
                        m.Invoke(api, args);
                        return;
                    }
                    catch { }
                }
            }
        }

        private static NurTagReading[] CollectTagsViaGetTagDataArray(object api)
        {
            if (api == null) return new NurTagReading[0];
            object cnt = TryInvokeReturn(api, "GetTagCount", null);
            if (cnt == null) return new NurTagReading[0];
            int count;
            try
            {
                count = Convert.ToInt32(cnt);
            }
            catch
            {
                return new NurTagReading[0];
            }
            if (count <= 0) return new NurTagReading[0];

            long ts = DateTime.UtcNow.Ticks / 10000L;
            var list = new ArrayList();
            for (int idx = 0; idx < count; idx++)
            {
                object tag = TryGetTagDataAt(api, idx);
                AddTagObjectToList(tag, list, ts);
            }
            var arr = new NurTagReading[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = (NurTagReading)list[i];
            return arr;
        }

        private static object TryGetTagDataAt(object api, int idx)
        {
            object tag = TryInvokeReturn(api, "GetTagData", new object[] { idx });
            if (tag != null) return tag;
            tag = TryInvokeReturn(api, "GetTagMeta", new object[] { idx });
            if (tag != null) return tag;
            return TryInvokeReturn(api, "GetTagDataEx", new object[] { idx });
        }

        private static NurTagReading[] CollectReadings(object tagStorage)
        {
            var list = new ArrayList();
            long ts = DateTime.UtcNow.Ticks / 10000L;
            try
            {
                IEnumerable enumerable = tagStorage as IEnumerable;
                if (enumerable != null)
                {
                    foreach (object tag in enumerable)
                    {
                        AddTagObjectToList(tag, list, ts);
                    }
                }

                if (list.Count == 0)
                {
                    Type st = tagStorage.GetType();
                    int count = GetCollectionCount(st, tagStorage);
                    for (int i = 0; i < count; i++)
                    {
                        object tag = GetCollectionItem(st, tagStorage, i);
                        AddTagObjectToList(tag, list, ts);
                    }
                }
            }
            catch { }
            var arr = new NurTagReading[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = (NurTagReading)list[i];
            return arr;
        }

        private static void AddTagObjectToList(object tag, ArrayList list, long ts)
        {
            if (tag == null || list == null) return;
            string epc = ExtractEpcFromTag(tag);
            if (epc.Length == 0) return;
            bool hasRssi;
            int rssi = ExtractRssiFromTag(tag, out hasRssi);
            var r = new NurTagReading();
            r.Epc = epc;
            r.Rssi = rssi;
            r.HasRssi = hasRssi;
            r.TimestampUtc = ts;
            list.Add(r);
        }

        private static int GetCollectionCount(Type t, object storage)
        {
            if (t == null || storage == null) return 0;
            object c = GetProp(t, storage, "Count");
            if (c == null) c = GetProp(t, storage, "Length");
            if (c == null) c = GetProp(t, storage, "TagCount");
            if (c != null)
            {
                try { return Convert.ToInt32(c); } catch { }
            }
            MethodInfo m = NurApiReflection.ResolveInstanceMethod(t, "GetTagCount", null);
            if (m != null)
            {
                try { return Convert.ToInt32(m.Invoke(storage, null)); } catch { }
            }
            return 0;
        }

        private static object GetCollectionItem(Type t, object storage, int index)
        {
            if (t == null || storage == null) return null;
            string[] methods = new string[] { "GetTag", "Get", "get_Item" };
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = NurApiReflection.ResolveInstanceMethod(t, methods[i], new object[] { index });
                if (m == null) continue;
                try { return m.Invoke(storage, new object[] { index }); } catch { }
            }
            return null;
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

        private const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static int ExtractRssiFromTag(object tag, out bool hasRssi)
        {
            hasRssi = false;
            if (tag == null) return 0;
            try
            {
                Type t = tag.GetType();
                int bestScore = -9999;
                int bestRssi = 0;

                TryScoreRssiField(t, tag, "IrRssi", 100, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "IRSSI", 100, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "irRssi", 100, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "LastRssi", 95, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "lastRssi", 95, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "Last_RSSI", 95, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "CurrentRssi", 90, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "currentRssi", 90, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "Rssi", 50, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "RSSI", 50, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "rssi", 50, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "m_Rssi", 50, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "SignalStrength", 40, ref bestScore, ref bestRssi, ref hasRssi);
                TryScoreRssiField(t, tag, "ReadStrength", 40, ref bestScore, ref bestRssi, ref hasRssi);

                PropertyInfo[] props = t.GetProperties(MemberFlags);
                for (int i = 0; i < props.Length; i++)
                {
                    string pn = props[i].Name;
                    int score = ScoreRssiFieldName(pn);
                    if (score < 0) continue;
                    object v = props[i].GetValue(tag, null);
                    int r;
                    bool has;
                    if (TryParseRssi(v, out has, out r) && has && score > bestScore)
                    {
                        bestScore = score;
                        bestRssi = r;
                        hasRssi = true;
                    }
                }

                FieldInfo[] fields = t.GetFields(MemberFlags);
                for (int i = 0; i < fields.Length; i++)
                {
                    string fn = fields[i].Name;
                    int score = ScoreRssiFieldName(fn);
                    if (score < 0) continue;
                    object v = fields[i].GetValue(tag);
                    int r;
                    bool has;
                    if (TryParseRssi(v, out has, out r) && has && score > bestScore)
                    {
                        bestScore = score;
                        bestRssi = r;
                        hasRssi = true;
                    }
                }

                if (hasRssi) return bestRssi;

                object scaled = GetMember(t, tag, "ScaledRssi");
                if (scaled == null) scaled = GetMember(t, tag, "scaledRssi");
                int scaledDbm;
                if (TryParseScaledRssi(scaled, out hasRssi, out scaledDbm)) return scaledDbm;
            }
            catch { }
            return 0;
        }

        private static void TryScoreRssiField(
            Type t, object tag, string name, int score,
            ref int bestScore, ref int bestRssi, ref bool hasRssi)
        {
            object v = GetMember(t, tag, name);
            int r;
            bool has;
            if (TryParseRssi(v, out has, out r) && has && score > bestScore)
            {
                bestScore = score;
                bestRssi = r;
                hasRssi = true;
            }
        }

        private static int ScoreRssiFieldName(string name)
        {
            if (name == null || name.Length == 0) return -1;
            string n = name.ToLower();
            if (n.IndexOf("max") >= 0 || n.IndexOf("peak") >= 0 || n.IndexOf("high") >= 0 || n.IndexOf("best") >= 0)
            {
                return -1;
            }
            if (n.IndexOf("irrssi") >= 0 || n == "ir_rssi") return 100;
            if (n.IndexOf("last") >= 0 && n.IndexOf("rssi") >= 0) return 95;
            if (n.IndexOf("current") >= 0 && n.IndexOf("rssi") >= 0) return 90;
            if (n.IndexOf("rssi") >= 0) return 50;
            if (n.IndexOf("signal") >= 0 || n.IndexOf("strength") >= 0) return 40;
            return -1;
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
                if (v is sbyte) { has = true; rssi = (int)(sbyte)v; return true; }
                if (v is byte) { has = true; rssi = (int)(byte)v; return true; }
                rssi = Convert.ToInt32(v);
                has = true;
                return true;
            }
            catch { }
            return false;
        }

        /// <summary>Map Nordic 0–100% scaled RSSI to approximate dBm when only scaled meta is present.</summary>
        private static bool TryParseScaledRssi(object v, out bool has, out int rssi)
        {
            has = false;
            rssi = 0;
            if (v == null) return false;
            try
            {
                int pct = Convert.ToInt32(v);
                if (pct < 0 || pct > 100) return false;
                rssi = -90 + (pct * 55) / 100;
                has = true;
                return true;
            }
            catch { }
            return false;
        }

        private static object GetMember(Type t, object o, string name)
        {
            object v = GetProp(t, o, name);
            if (v != null) return v;
            return GetField(t, o, name);
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
            PropertyInfo p = t.GetProperty(name, MemberFlags);
            if (p == null) return null;
            return p.GetValue(o, null);
        }

        private static object GetField(Type t, object o, string name)
        {
            FieldInfo f = t.GetField(name, MemberFlags);
            if (f == null) return null;
            return f.GetValue(o);
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
            if (_streamRestartTimer != null)
            {
                _streamRestartTimer.Enabled = false;
                _streamRestartTimer.Dispose();
                _streamRestartTimer = null;
            }
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
