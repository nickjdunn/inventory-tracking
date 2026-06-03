using System;
using System.Collections;
using System.Drawing;
using System.Windows.Forms;

namespace MerlinAudit
{
    /// <summary>
    /// Automated RF preset sweep over a tag pile (no target EPC required).
    /// </summary>
    internal sealed class RfBenchForm : Form
    {
        private const int StateIdle = 0;
        private const int StateApplyWait = 1;
        private const int StatePulseWait = 2;
        private const int StatePresetGap = 3;
        private const int StateDiag = 4;
        private const int StateDone = 5;
        private const int StateCountdown = 6;
        private const int BenchCountdownSeconds = 3;

        private readonly AuditConfig _cfg;
        private readonly AuditClient _client;
        private readonly NurApiBridge _nur;
        private readonly Label _status;
        private readonly Label _hint;
        private readonly Label _stackLabel;
        private readonly TextBox _targetBox;
        private readonly Label _moduleSetupLabel;
        private readonly Label _progress;
        private readonly Label _countdownBanner;
        private readonly ListBox _rankList;
        private RfBenchStackSetup _stackSetup;
        private readonly System.Windows.Forms.Timer _benchTimer;
        private int _countdownSeconds;

        private readonly ArrayList _presetResults = new ArrayList();
        private readonly ArrayList _diagResults = new ArrayList();
        private int _benchState = StateIdle;
        private int _presetIndex;
        private int _pulseIndex;
        private int _waitTicks;
        private int _pulsesPerPreset = 8;
        private RfBenchPresetResult _currentResult;
        private NurRfPreset _currentPreset;
        private NurRfPreset _bestPreset;
        private bool _running;
        private bool _triggerHeld;
        private int _pulseCooldownTicks;
        private bool _pileAccumActive;
        private int _accumulateTicks;
        private bool _idleTestAccumActive;
        private NurModuleSetupSnapshot _baselineSetup;

        public RfBenchForm(AuditConfig cfg)
        {
            _cfg = cfg ?? new AuditConfig();
            _client = new AuditClient(_cfg);
            _pulsesPerPreset = _cfg.BenchPulsesPerPreset;
            if (_pulsesPerPreset < 4) _pulsesPerPreset = 4;
            if (_pulsesPerPreset > 16) _pulsesPerPreset = 16;
            _accumulateTicks = _cfg.BenchAccumulateTicks;
            if (_accumulateTicks < 6) _accumulateTicks = 6;
            if (_accumulateTicks > 20) _accumulateTicks = 20;

            UiTheme.ApplyForm(this);
            Text = "RF bench";
            Width = 240;
            Height = 320;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            _nur = new NurApiBridge(this, _cfg);
            _nur.TagReadingsReady += OnBenchTagReadings;

            _hint = UiTheme.MakeHint(
                "Miller sweep — 6=stack · 1=run · 3=test · 7=RF");
            CfLayout.Place(_hint, 6, 4, 228, 24);

            _stackSetup = new RfBenchStackSetup();
            _stackSetup.LoadFromConfig(_cfg);
            _stackLabel = UiTheme.MakeHint("Stack: " + _stackSetup.SummaryLine());
            CfLayout.Place(_stackLabel, 6, 28, 228, 14);

            _moduleSetupLabel = UiTheme.MakeHint("Module setup: …");
            CfLayout.Place(_moduleSetupLabel, 6, 42, 228, 34);

            var lblT = UiTheme.MakeHint("Optional target EPC");
            CfLayout.Place(lblT, 6, 76, 228, 10);

            _targetBox = UiTheme.MakeField("");
            CfLayout.Place(_targetBox, 6, 86, 228, 18);

            _progress = UiTheme.MakeHint("1=run · 3=test · 7=read RF · 4=upload");
            CfLayout.Place(_progress, 6, 106, 228, 12);

            _status = UiTheme.MakeHeader("NUR starting…");
            CfLayout.Place(_status, 6, 120, 228, 24);

            _countdownBanner = new Label
            {
                ForeColor = UiTheme.Warn,
                BackColor = UiTheme.Card,
                Font = new Font("Tahoma", 16f, FontStyle.Bold),
                TextAlign = ContentAlignment.TopCenter,
                Text = "",
                Visible = false,
            };
            CfLayout.Place(_countdownBanner, 6, 144, 228, 36);

            _rankList = new ListBox
            {
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                BackColor = UiTheme.Card,
                ForeColor = UiTheme.Text,
            };
            CfLayout.Place(_rankList, 6, 182, 228, 90);

            Controls.Add(_rankList);
            Controls.Add(_countdownBanner);
            Controls.Add(_status);
            Controls.Add(_progress);
            Controls.Add(_targetBox);
            Controls.Add(_moduleSetupLabel);
            Controls.Add(_stackLabel);
            Controls.Add(_hint);
            Controls.Add(lblT);

            _benchTimer = new System.Windows.Forms.Timer();
            _benchTimer.Interval = 200;
            _benchTimer.Tick += BenchTimer_Tick;

            KeyDown += RfBenchForm_KeyDown;
            KeyUp += RfBenchForm_KeyUp;
            Load += delegate
            {
                _nur.Start();
                _nur.EnsureInventoryStream();
                _status.Text = _nur.Status;
                if (_targetBox.Text.Length == 0 && RssiTraceRecorder.TargetEpc.Length > 0)
                {
                    _targetBox.Text = RssiTraceRecorder.TargetEpc;
                }
                RefreshModuleSetupDisplay(true);
                TestSessionLog.Add("rf_bench", "opened", _nur.Status);
            };
            Closed += delegate
            {
                _benchTimer.Enabled = false;
                _running = false;
                EndTriggerHold();
                _nur.Dispose();
            };
        }

        private void RfBenchForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1 || e.KeyCode == Keys.F9)
            {
                OnTriggerDown();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { Close(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) { StartBench(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) { StopBench(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) { QuickScanPile(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4) { UploadResults(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D5 || e.KeyCode == Keys.NumPad5) { SaveLocal(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D6 || e.KeyCode == Keys.NumPad6) { EditStackSetup(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D7 || e.KeyCode == Keys.NumPad7) { RefreshModuleSetupDisplay(true); e.Handled = true; return; }
        }

        private void RefreshModuleSetupDisplay(bool log)
        {
            if (!_nur.IsAvailable)
            {
                _moduleSetupLabel.Text = _nur.Status;
                return;
            }
            NurModuleSetupSnapshot snap = _nur.ReadModuleSetup();
            _moduleSetupLabel.Text = "RF: " + snap.ToDetailLine();
            if (log)
            {
                TestSessionLog.Add("module_setup", snap.ToCompactLine(),
                    snap.ReadOk ? "" : snap.ReadError);
            }
        }

        private static NurModuleSetupSnapshot SnapshotFromPreset(NurRfPreset preset)
        {
            var snap = new NurModuleSetupSnapshot();
            snap.ReadOk = true;
            if (preset == null) return snap;
            snap.LinkFreqHz = preset.LinkFreqHz;
            snap.TxLevel = preset.TxLevel;
            snap.RxDecoding = preset.RxDecoding;
            snap.TxModulation = preset.TxModulation;
            return snap;
        }

        private void CapturePresetModuleSetup(NurRfPreset preset, RfBenchPresetResult result)
        {
            if (preset == null || result == null) return;
            result.RequestedRxDecoding = preset.RxDecoding;
            result.RequestedTxModulation = preset.TxModulation;
            result.ModuleSetupRequested = SnapshotFromPreset(preset);
            NurModuleSetupSnapshot read = _nur.ReadModuleSetup();
            result.ModuleSetupReadBack = read;
            result.ReadRxDecoding = read.RxDecoding;
            result.ReadTxModulation = read.TxModulation;
            RefreshModuleSetupDisplay(true);
        }

        private void RfBenchForm_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1 || e.KeyCode == Keys.F9)
            {
                EndTriggerHold();
                e.Handled = true;
            }
        }

        private void OnTriggerDown()
        {
            if (_running || !_nur.IsAvailable)
            {
                if (!_nur.IsAvailable) _status.Text = _nur.Status;
                return;
            }
            if (!_triggerHeld)
            {
                _triggerHeld = true;
                _nur.PollOnlyMode = false;
                _nur.EnsureInventoryStream();
            }
            _nur.TriggerInventory();
        }

        private void OnBenchTagReadings(object sender, NurTagReadingsEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { OnBenchTagReadings(sender, e); }), null, EventArgs.Empty);
                return;
            }
            if (e == null || e.Readings == null) return;
            if (_pileAccumActive)
            {
                _nur.MergePileSampleReadings(e.Readings);
                _progress.Text = "Scan… " + _nur.PileSampleTagCount() + " tags";
                return;
            }
            if (_running || _idleTestAccumActive) return;
            ShowPileScanStatus(e.Readings);
        }

        private void EndTriggerHold()
        {
            if (!_triggerHeld) return;
            _triggerHeld = false;
            if (!_running && !_idleTestAccumActive)
            {
                _nur.PollOnlyMode = false;
                _nur.StopInventoryStreamSafe();
            }
        }

        private void RefreshStackLabel()
        {
            if (_stackSetup == null) _stackSetup = new RfBenchStackSetup();
            _stackLabel.Text = "Stack: " + _stackSetup.SummaryLine();
        }

        private bool EditStackSetup()
        {
            var dlg = new RfBenchStackSetupForm(_stackSetup);
            try
            {
                DialogResult dr = dlg.ShowDialog();
                if (dr != DialogResult.OK || dlg.ResultSetup == null)
                {
                    _status.Text = "Stack setup cancelled";
                    return false;
                }
                _stackSetup = dlg.ResultSetup;
                _stackSetup.SaveToConfig(_cfg);
                RefreshStackLabel();
                TestSessionLog.Add("rf_bench_stack", _stackSetup.SummaryLine(), "");
                _status.Text = "Stack OK — 1=run (3-2-1)";
                return true;
            }
            finally
            {
                dlg.Dispose();
            }
        }

        private string TargetEpc
        {
            get { return RssiTraceRecorder.NormalizeEpc(_targetBox.Text); }
        }

        private void StartBench()
        {
            if (_running) return;
            if (_stackSetup == null)
            {
                _stackSetup = new RfBenchStackSetup();
            }
            _stackSetup.LoadFromConfig(_cfg);
            RefreshStackLabel();

            if (!_nur.IsAvailable)
            {
                _status.Text = _nur.Status;
                return;
            }

            _nur.PollOnlyMode = false;
            _nur.EnsureInventoryStream();
            _pileAccumActive = false;
            _baselineSetup = _nur.ReadModuleSetup();
            RefreshModuleSetupDisplay(true);

            _presetResults.Clear();
            _diagResults.Clear();
            _rankList.Items.Clear();
            _presetIndex = 0;
            _pulseIndex = 0;
            _waitTicks = 0;
            _bestPreset = null;
            _running = true;
            _countdownSeconds = BenchCountdownSeconds;
            _benchState = StateCountdown;
            _countdownBanner.Visible = true;
            _countdownBanner.Text = _countdownSeconds.ToString();
            _countdownBanner.BringToFront();
            _benchTimer.Interval = 1000;
            _benchTimer.Enabled = true;
            _status.Text = "Set stack — start in " + _countdownSeconds;
            string targetNote = TargetEpc;
            TestSessionLog.Add("rf_bench_run", _stackSetup.SummaryLine(),
                targetNote.Length > 0 ? targetNote : "pile");
            CeAudio.Click();
        }

        private void StopBench()
        {
            _running = false;
            _pileAccumActive = false;
            _idleTestAccumActive = false;
            _nur.PollOnlyMode = false;
            _benchState = StateIdle;
            _benchTimer.Enabled = false;
            _benchTimer.Interval = 200;
            _countdownBanner.Visible = false;
            _status.Text = "Stopped";
        }

        private void QuickScanPile()
        {
            if (!_nur.IsAvailable || _running) return;
            _status.Text = "Test scan…";
            _nur.PollOnlyMode = false;
            _nur.OpenPileSample(true);
            _pileAccumActive = true;
            _idleTestAccumActive = true;
            _pulseCooldownTicks = _accumulateTicks;
            _benchTimer.Interval = 200;
            _benchTimer.Enabled = true;
        }

        private void ShowPileScanStatus(NurTagReading[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                _status.Text = "No tags — F1/trigger or 3=test";
                return;
            }
            int bestRssi = -999;
            int rssiN = 0;
            int rssiSum = 0;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == null || !tags[i].HasRssi) continue;
                rssiSum += tags[i].Rssi;
                rssiN++;
                if (tags[i].Rssi > bestRssi) bestRssi = tags[i].Rssi;
            }
            string rssiPart = rssiN > 0
                ? (" best=" + bestRssi + " avg=" + (rssiSum / rssiN))
                : "";
            _status.Text = tags.Length + " tags" + rssiPart;
        }

        private string PulseModeName()
        {
            if (_currentPreset != null && _currentPreset.UseEpcSelect && TargetEpc.Length > 0)
            {
                return "pile_epc_filter";
            }
            return "pile_stream_accum";
        }

        private void BenchTimer_Tick(object sender, EventArgs e)
        {
            if (_idleTestAccumActive)
            {
                TickIdleTestAccumulate();
                return;
            }
            if (!_running) return;

            switch (_benchState)
            {
                case StateCountdown:
                    TickCountdown();
                    break;
                case StateApplyWait:
                    TickApplyPreset();
                    break;
                case StatePulseWait:
                    TickPulse();
                    break;
                case StatePresetGap:
                    TickPresetGap();
                    break;
                case StateDiag:
                    TickDiag();
                    break;
                case StateDone:
                    FinishBench();
                    break;
            }
        }

        private void TickCountdown()
        {
            CeAudio.Click();
            if (_countdownSeconds <= 1)
            {
                _countdownBanner.Visible = false;
                _benchTimer.Interval = 200;
                _benchState = StateApplyWait;
                _waitTicks = 0;
                _status.Text = "Bench: " + _stackSetup.SummaryLine();
                return;
            }
            _countdownSeconds--;
            _countdownBanner.Text = _countdownSeconds.ToString();
            _status.Text = "Set stack — start in " + _countdownSeconds;
        }

        private void TickApplyPreset()
        {
            if (_waitTicks == 0)
            {
                if (_presetIndex >= NurRfPresets.Count)
                {
                    _benchState = StateDiag;
                    _waitTicks = 0;
                    PickBestPreset();
                    return;
                }
                _currentPreset = NurRfPresets.Get(_presetIndex);
                _currentResult = new RfBenchPresetResult();
                _currentResult.PresetId = _currentPreset.Id;
                _currentResult.Label = _currentPreset.Label;
                _currentResult.LinkFreqHz = _currentPreset.LinkFreqHz;
                _currentResult.TxLevel = _currentPreset.TxLevel;
                _currentResult.UseEpcSelect = _currentPreset.UseEpcSelect;
                _currentResult.Pulses = _pulsesPerPreset;
                NurProfileStatus prof = _nur.ApplyRfPreset(_currentPreset);
                _nur.EnsureInventoryStream();
                CapturePresetModuleSetup(_currentPreset, _currentResult);
                _currentResult.ApplyOk = prof.ApplyOk;
                _currentResult.ReadLinkFreqHz = prof.ReadLinkFreqHz;
                _currentResult.ReadTxLevel = prof.ReadTxLevel;
                _pulseIndex = 0;
                _status.Text = "Apply " + (_presetIndex + 1) + "/" + NurRfPresets.Count
                    + " " + _currentPreset.ShortLabel();
            }
            _waitTicks++;
            if (_waitTicks >= 12)
            {
                _waitTicks = 0;
                _pulseCooldownTicks = 0;
                _benchState = StatePulseWait;
            }
        }

        private void TickPulse()
        {
            NurTagReading[] all = null;

            if (_pileAccumActive)
            {
                if (_pulseCooldownTicks > 0)
                {
                    _pulseCooldownTicks--;
                    _nur.TickPileSample();
                    _progress.Text = "Scan… " + _nur.PileSampleTagCount() + " tags";
                    return;
                }
                _pileAccumActive = false;
                all = _nur.ClosePileSample();
                RecordPulseSample(all);
            }
            else if (_pulseCooldownTicks > 0)
            {
                _pulseCooldownTicks--;
                return;
            }
            else
            {
                _nur.OpenPileSample(true);
                _pileAccumActive = true;
                _pulseCooldownTicks = _accumulateTicks;
                _status.Text = "Scan " + (_pulseIndex + 1) + "/" + _pulsesPerPreset;
                return;
            }

            _pulseIndex++;
            if (_pulseIndex >= _pulsesPerPreset)
            {
                _currentResult.RecomputeScore();
                _presetResults.Add(_currentResult);
                AppendRankLine(_currentResult);
                _presetIndex++;
                _benchState = StatePresetGap;
                return;
            }

            _progress.Text = "Pulse " + _pulseIndex + "/" + _pulsesPerPreset
                + " last=" + (all != null ? all.Length : 0) + " tags";
            _pulseCooldownTicks = 3;
        }

        private void TickIdleTestAccumulate()
        {
            if (_pulseCooldownTicks > 0)
            {
                _pulseCooldownTicks--;
                _nur.TickPileSample();
                _progress.Text = "Test… " + _nur.PileSampleTagCount() + " tags";
                return;
            }
            _idleTestAccumActive = false;
            _pileAccumActive = false;
            _benchTimer.Enabled = false;
            NurTagReading[] tags = _nur.ClosePileSample();
            if (!_running)
            {
                _nur.PollOnlyMode = false;
                _nur.StopInventoryStreamSafe();
            }
            ShowPileScanStatus(tags);
        }

        private void RecordPulseSample(NurTagReading[] all)
        {
            if (all == null) all = new NurTagReading[0];
            var sample = new RfBenchPulseSample();
            sample.Pulse = _pulseIndex + 1;
            RfBenchPileMetrics.RecordRound(
                all, TargetEpc, _currentResult, sample, PulseModeName());
            _currentResult.PulseLog.Add(sample);
        }

        private void TickPresetGap()
        {
            _waitTicks++;
            if (_waitTicks >= 4)
            {
                _waitTicks = 0;
                _benchState = StateApplyWait;
            }
        }

        private void PickBestPreset()
        {
            int bestScore = -99999;
            for (int i = 0; i < _presetResults.Count; i++)
            {
                RfBenchPresetResult r = _presetResults[i] as RfBenchPresetResult;
                if (r == null) continue;
                r.RecomputeScore();
                if (r.Score > bestScore)
                {
                    bestScore = r.Score;
                    for (int p = 0; p < NurRfPresets.Count; p++)
                    {
                        if (NurRfPresets.Get(p).Id == r.PresetId)
                        {
                            _bestPreset = NurRfPresets.Get(p);
                            break;
                        }
                    }
                }
            }
        }

        private void TickDiag()
        {
            if (_bestPreset == null || _waitTicks > 0)
            {
                _benchState = StateDone;
                _waitTicks = 0;
                return;
            }
            if (_waitTicks == 0)
            {
                _nur.ApplyRfPreset(_bestPreset);
                _nur.EnsureInventoryStream();
            }
            else if (_waitTicks == 3)
            {
                RunDiagPileTest("pile_confirm", 6);
            }
            else if (_waitTicks >= 7)
            {
                _benchState = StateDone;
                _waitTicks = 0;
                return;
            }
            _waitTicks++;
        }

        private void RunDiagPileTest(string testId, int pulses)
        {
            int hits = 0;
            int tagSum = 0;
            int rssiSum = 0;
            int rssiN = 0;
            for (int i = 0; i < pulses; i++)
            {
                _nur.OpenPileSample(i == 0);
                for (int t = 0; t < _accumulateTicks; t++)
                {
                    System.Threading.Thread.Sleep(200);
                    _nur.TickPileSample();
                }
                NurTagReading[] all = _nur.ClosePileSample();
                if (all != null && all.Length > 0)
                {
                    hits++;
                    tagSum += all.Length;
                    for (int t = 0; t < all.Length; t++)
                    {
                        if (all[t] == null || !all[t].HasRssi) continue;
                        rssiSum += all[t].Rssi;
                        rssiN++;
                    }
                }
            }
            var d = new RfBenchDiagResult();
            d.TestId = testId;
            d.Pulses = pulses;
            d.Hits = hits;
            d.AvgTags = pulses > 0 ? (tagSum / pulses) : 0;
            d.AvgRssi = rssiN > 0 ? (rssiSum / rssiN) : -999;
            d.Detail = "open inventory pile read";
            _diagResults.Add(d);
            _waitTicks = 6;
        }

        private void FinishBench()
        {
            _running = false;
            _pileAccumActive = false;
            _nur.PollOnlyMode = false;
            _benchTimer.Enabled = false;
            _benchTimer.Interval = 200;
            _countdownBanner.Visible = false;
            _nur.EnsureInventoryStream();
            PickBestPreset();
            if (_bestPreset != null)
            {
                _cfg.RfPresetIndex = NurRfPresets.NormalizeIndex(
                    IndexOfPreset(_bestPreset.Id));
                _cfg.Save();
                _nur.ApplyRfPreset(_bestPreset);
            }
            _status.Text = "Done — 4=upload";
            CeAudio.FoundTone();
            TestSessionLog.Add("rf_bench", "done", RankSummaryLine());
        }

        private int IndexOfPreset(string id)
        {
            for (int i = 0; i < NurRfPresets.Count; i++)
            {
                if (NurRfPresets.Get(i).Id == id) return i;
            }
            return 0;
        }

        private void AppendRankLine(RfBenchPresetResult r)
        {
            r.RecomputeScore();
            string line = r.AvgTagsPerPulse + " tags/p sc=" + r.Score + " " + r.PresetId;
            if (r.ReadRxDecoding >= 0)
            {
                line += " " + NurModuleSetupSnapshot.RxDecodingLabel(r.ReadRxDecoding);
            }
            if (r.AvgRssi > -900) line += " rssi=" + r.AvgRssi;
            if (r.TargetHits > 0) line += " tgt=" + r.TargetHits;
            _rankList.Items.Add(line);
        }

        private string RankSummaryLine()
        {
            if (_rankList.Items.Count == 0) return "no results";
            return _rankList.Items[0].ToString();
        }

        private string BuildSessionJson()
        {
            string notes = "tx_level 0 is max power. Sweep: link freq x Miller RX (FM0/M2/M4/M8).";
            if (_bestPreset != null)
            {
                notes += " best=" + _bestPreset.Id;
            }
            return RfBenchJson.ToJson(
                "pile", TargetEpc, _pulsesPerPreset, _stackSetup,
                _presetResults, _diagResults, notes, _baselineSetup);
        }

        private void SaveLocal()
        {
            string json = BuildSessionJson();
            RfBenchJson.SavePending(json);
            _status.Text = "Saved local bench JSON";
        }

        private void UploadResults()
        {
            string session = BuildSessionJson();
            if (session.IndexOf("\"bench_results\":[]") >= 0)
            {
                string pending = RfBenchJson.LoadPending();
                if (pending.Length > 0) session = pending;
            }
            if (session.IndexOf("\"bench_results\":[]") >= 0)
            {
                _status.Text = "Run bench first (1)";
                return;
            }
            HttpResult res = _client.UploadRssiTrace(session);
            if (res.Ok)
            {
                RfBenchJson.ClearPending();
                _status.Text = "Uploaded OK";
                TestSessionLog.Add("rf_bench", "upload", res.Body);
            }
            else
            {
                RfBenchJson.SavePending(session);
                _status.Text = "Upload failed — saved local";
            }
        }
    }
}
