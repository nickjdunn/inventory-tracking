using System;
using System.Collections;
using System.Drawing;
using System.Windows.Forms;

namespace MerlinAudit
{
    /// <summary>
    /// Automated RF preset sweep: hold target tag still, run bench, upload ranked JSON.
    /// </summary>
    internal sealed class RfBenchForm : Form
    {
        private const int StateIdle = 0;
        private const int StateApplyWait = 1;
        private const int StatePulseWait = 2;
        private const int StatePresetGap = 3;
        private const int StateDiag = 4;
        private const int StateDone = 5;

        private readonly AuditConfig _cfg;
        private readonly AuditClient _client;
        private readonly NurApiBridge _nur;
        private readonly Label _status;
        private readonly Label _hint;
        private readonly Label _stackLabel;
        private readonly TextBox _targetBox;
        private readonly Label _progress;
        private readonly ListBox _rankList;
        private RfBenchStackSetup _stackSetup;
        private readonly System.Windows.Forms.Timer _benchTimer;

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

        public RfBenchForm(AuditConfig cfg)
        {
            _cfg = cfg ?? new AuditConfig();
            _client = new AuditClient(_cfg);
            _pulsesPerPreset = _cfg.BenchPulsesPerPreset;
            if (_pulsesPerPreset < 4) _pulsesPerPreset = 4;
            if (_pulsesPerPreset > 16) _pulsesPerPreset = 16;

            UiTheme.ApplyForm(this);
            Text = "RF bench";
            Width = 240;
            Height = 320;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            _nur = new NurApiBridge(this, _cfg);
            _nur.PollOnlyMode = true;

            _hint = UiTheme.MakeHint(
                "Tag stack in front of reader. 6=stack setup before run.");
            CfLayout.Place(_hint, 6, 4, 228, 24);

            _stackSetup = new RfBenchStackSetup();
            _stackSetup.LoadFromConfig(_cfg);
            _stackLabel = UiTheme.MakeHint("Stack: " + _stackSetup.SummaryLine());
            CfLayout.Place(_stackLabel, 6, 28, 228, 14);

            var lblT = UiTheme.MakeHint("Target EPC in stack (hex)");
            CfLayout.Place(lblT, 6, 44, 228, 12);

            _targetBox = UiTheme.MakeField("");
            CfLayout.Place(_targetBox, 6, 56, 228, 20);

            _progress = UiTheme.MakeHint("1=run 6=stack 3=scan 4=upload");
            CfLayout.Place(_progress, 6, 78, 228, 12);

            _status = UiTheme.MakeHeader("NUR starting…");
            CfLayout.Place(_status, 6, 92, 228, 28);

            _rankList = new ListBox
            {
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                BackColor = UiTheme.Card,
                ForeColor = UiTheme.Text,
            };
            CfLayout.Place(_rankList, 6, 122, 228, 150);

            _benchTimer = new System.Windows.Forms.Timer();
            _benchTimer.Interval = 120;
            _benchTimer.Tick += BenchTimer_Tick;

            KeyDown += RfBenchForm_KeyDown;
            Load += delegate
            {
                _nur.Start();
                _nur.EnsureInventoryStream();
                _status.Text = _nur.Status;
                if (_targetBox.Text.Length == 0 && RssiTraceRecorder.TargetEpc.Length > 0)
                {
                    _targetBox.Text = RssiTraceRecorder.TargetEpc;
                }
                TestSessionLog.Add("rf_bench", "opened", _nur.Status);
            };
            Closed += delegate
            {
                _benchTimer.Enabled = false;
                _running = false;
                _nur.Dispose();
            };
        }

        private void RfBenchForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { Close(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) { StartBench(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) { StopBench(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) { QuickScanTarget(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4) { UploadResults(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D5 || e.KeyCode == Keys.NumPad5) { SaveLocal(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D6 || e.KeyCode == Keys.NumPad6) { EditStackSetup(); e.Handled = true; return; }
        }

        private void RefreshStackLabel()
        {
            if (_stackSetup == null) _stackSetup = new RfBenchStackSetup();
            _stackLabel.Text = "Stack: " + _stackSetup.SummaryLine();
        }

        private bool PromptStackSetup()
        {
            return EditStackSetup();
        }

        private bool EditStackSetup()
        {
            using (var dlg = new RfBenchStackSetupForm(_stackSetup))
            {
                if (dlg.ShowDialog() != DialogResult.OK || dlg.ResultSetup == null)
                {
                    return false;
                }
                _stackSetup = dlg.ResultSetup;
                _stackSetup.SaveToConfig(_cfg);
                RefreshStackLabel();
                TestSessionLog.Add("rf_bench_stack", _stackSetup.SummaryLine(), "");
                _status.Text = "Stack logged: " + _stackSetup.SummaryLine();
                return true;
            }
        }

        private string TargetEpc
        {
            get { return RssiTraceRecorder.NormalizeEpc(_targetBox.Text); }
        }

        private void StartBench()
        {
            if (_running) return;
            if (!PromptStackSetup()) return;
            string target = TargetEpc;
            if (target.Length == 0)
            {
                _status.Text = "Enter target EPC (3=scan)";
                return;
            }
            if (!_nur.IsAvailable)
            {
                _status.Text = _nur.Status;
                return;
            }

            _presetResults.Clear();
            _diagResults.Clear();
            _rankList.Items.Clear();
            _presetIndex = 0;
            _pulseIndex = 0;
            _waitTicks = 0;
            _bestPreset = null;
            _running = true;
            _benchState = StateApplyWait;
            _benchTimer.Enabled = true;
            _status.Text = "Bench: " + _stackSetup.SummaryLine();
            TestSessionLog.Add("rf_bench_run", _stackSetup.SummaryLine(), target);
            CeAudio.Click();
        }

        private void StopBench()
        {
            _running = false;
            _benchState = StateIdle;
            _benchTimer.Enabled = false;
            _status.Text = "Stopped";
        }

        private void QuickScanTarget()
        {
            if (!_nur.IsAvailable) return;
            _nur.TriggerInventory();
            NurTagReading[] tags = _nur.ForceFetchTags();
            if (tags == null || tags.Length == 0)
            {
                _status.Text = "No tags — hold trigger";
                return;
            }
            NurTagReading best = tags[0];
            for (int i = 1; i < tags.Length; i++)
            {
                if (tags[i] == null) continue;
                if (!tags[i].HasRssi || !best.HasRssi)
                {
                    if (tags[i].Epc.Length > best.Epc.Length) best = tags[i];
                    continue;
                }
                if (tags[i].Rssi > best.Rssi) best = tags[i];
            }
            _targetBox.Text = best.Epc;
            _status.Text = "Target " + EpcDisplay.Suffix(best.Epc, 8)
                + (best.HasRssi ? (" @" + best.Rssi) : "");
        }

        private void BenchTimer_Tick(object sender, EventArgs e)
        {
            if (!_running) return;

            switch (_benchState)
            {
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
                _currentResult.ApplyOk = prof.ApplyOk;
                _currentResult.ReadLinkFreqHz = prof.ReadLinkFreqHz;
                _currentResult.ReadTxLevel = prof.ReadTxLevel;
                _pulseIndex = 0;
                _status.Text = "Apply " + (_presetIndex + 1) + "/" + NurRfPresets.Count
                    + " " + _currentPreset.ShortLabel();
            }
            _waitTicks++;
            if (_waitTicks >= 3)
            {
                _waitTicks = 0;
                _benchState = StatePulseWait;
            }
        }

        private void TickPulse()
        {
            if (_waitTicks == 0)
            {
                _nur.BenchInventoryPulse(TargetEpc, _currentPreset.UseEpcSelect);
            }
            else if (_waitTicks == 2)
            {
                RecordPulseSample();
                _pulseIndex++;
                if (_pulseIndex >= _pulsesPerPreset)
                {
                    _currentResult.RecomputeScore();
                    _presetResults.Add(_currentResult);
                    AppendRankLine(_currentResult);
                    _presetIndex++;
                    _benchState = StatePresetGap;
                    _waitTicks = 0;
                    return;
                }
            }
            _waitTicks++;
            if (_waitTicks >= 4)
            {
                _waitTicks = 0;
            }
            _progress.Text = "Pulse " + (_pulseIndex + 1) + "/" + _pulsesPerPreset
                + " hit%=" + _currentResult.HitPercent;
        }

        private void RecordPulseSample()
        {
            string target = TargetEpc;
            NurTagReading[] targetHits = _nur.FetchTrackRoundTags(target);
            NurTagReading[] all = _nur.FetchRoundAllTags();
            bool seen = targetHits != null && targetHits.Length > 0;
            int rssi = -999;
            bool hasRssi = false;
            if (seen)
            {
                rssi = targetHits[0].Rssi;
                hasRssi = targetHits[0].HasRssi;
            }
            int others = 0;
            if (all != null)
            {
                string norm = RssiTraceRecorder.NormalizeEpc(target);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    if (RssiTraceRecorder.NormalizeEpc(all[i].Epc) != norm) others++;
                }
            }

            var sample = new RfBenchPulseSample();
            sample.Pulse = _pulseIndex + 1;
            sample.TargetSeen = seen;
            sample.TargetRssi = rssi;
            sample.TargetHasRssi = hasRssi;
            sample.OtherTagCount = others;
            sample.Mode = _currentPreset.UseEpcSelect ? "round_epc" : "round_open";
            _currentResult.PulseLog.Add(sample);

            if (seen)
            {
                _currentResult.Hits++;
                if (hasRssi)
                {
                    _currentResult.RssiSum += rssi;
                    _currentResult.RssiCount++;
                    if (rssi > _currentResult.BestRssi) _currentResult.BestRssi = rssi;
                    if (_currentResult.WorstRssi == 0 || rssi < _currentResult.WorstRssi)
                    {
                        _currentResult.WorstRssi = rssi;
                    }
                }
            }
            else
            {
                _currentResult.Misses++;
            }
            _currentResult.OtherTagsTotal += others;
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
            }
            else if (_waitTicks == 3)
            {
                RunDiagTest("track_round", true, 6);
            }
            else if (_waitTicks == 7)
            {
                RunDiagTest("list_force", false, 6);
            }
            else if (_waitTicks >= 11)
            {
                _benchState = StateDone;
                _waitTicks = 0;
                return;
            }
            _waitTicks++;
        }

        private void RunDiagTest(string testId, bool useRoundEpc, int pulses)
        {
            string target = TargetEpc;
            int hits = 0;
            int rssiSum = 0;
            int rssiN = 0;
            for (int i = 0; i < pulses; i++)
            {
                if (useRoundEpc)
                {
                    _nur.BenchInventoryPulse(target, true);
                    System.Threading.Thread.Sleep(90);
                    NurTagReading[] t = _nur.FetchTrackRoundTags(target);
                    if (t != null && t.Length > 0)
                    {
                        hits++;
                        if (t[0].HasRssi) { rssiSum += t[0].Rssi; rssiN++; }
                    }
                }
                else
                {
                    _nur.BenchInventoryPulse(target, false);
                    System.Threading.Thread.Sleep(90);
                    NurTagReading[] all = _nur.ForceFetchTags();
                    NurTagReading[] t = NurReaderProfile.FilterExactEpc(all, target);
                    if (t != null && t.Length > 0)
                    {
                        hits++;
                        if (t[0].HasRssi) { rssiSum += t[0].Rssi; rssiN++; }
                    }
                }
            }
            var d = new RfBenchDiagResult();
            d.TestId = testId;
            d.Pulses = pulses;
            d.Hits = hits;
            d.AvgRssi = rssiN > 0 ? (rssiSum / rssiN) : -999;
            d.Detail = useRoundEpc ? "EPC round read" : "ForceFetch list";
            _diagResults.Add(d);
            _waitTicks = 10;
        }

        private void FinishBench()
        {
            _running = false;
            _benchTimer.Enabled = false;
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
            string line = r.HitPercent + "% sc=" + r.Score + " " + r.PresetId;
            if (r.AvgRssi > -900) line += " avg=" + r.AvgRssi;
            _rankList.Items.Add(line);
        }

        private string RankSummaryLine()
        {
            if (_rankList.Items.Count == 0) return "no results";
            return _rankList.Items[0].ToString();
        }

        private string BuildSessionJson()
        {
            string notes = "tx_level 0 is max power; higher values attenuate. Cannot exceed reader cap.";
            if (_bestPreset != null)
            {
                notes += " best=" + _bestPreset.Id;
            }
            return RfBenchJson.ToJson(
                TargetEpc, _pulsesPerPreset, _stackSetup, _presetResults, _diagResults, notes);
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
