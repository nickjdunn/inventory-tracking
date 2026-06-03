using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MerlinAudit
{
    internal sealed class RssiTrackForm : Form
    {
        private readonly AuditConfig _cfg;
        private readonly AuditClient _client;
        private readonly NurApiBridge _nur;
        private readonly Label _status;
        private readonly Label _rfProfile;
        private readonly Label _target;
        private readonly ListBox _tagList;
        private readonly Panel _trackPanel;
        private readonly Label _rssiBig;
        private readonly Label _closeLabel;
        private readonly Panel _barTrack;
        private readonly Panel _barFill;
        private readonly Label _rec;
        private readonly Label _signalBanner;
        private readonly Label _lastSeenLabel;
        private readonly Button _btnScan;
        private readonly Button _btnTrack;
        private readonly RssiGeigerAudio _geiger;
        private readonly System.Windows.Forms.Timer _recTimer;
        private readonly System.Windows.Forms.Timer _uiTimer;

        private RssiListEntry[] _listEntries = new RssiListEntry[0];
        private bool _triggerHeld;
        private bool _inTrackView;
        private bool _trackActive;
        private bool _listOrderDirty;
        private long _lastFullListPaintTicks;
        private long _lastUiTicks;
        private long _lastRecordTicks;
        private int _lastPaintedTrackRssi = -999;
        private int _holdRssi;
        private bool _holdHasRssi;
        private long _holdRssiTicks;
        private long _lastSeenTicks;
        private bool _signalLive;
        private bool _hadLiveSignal;
        private bool _lostCueSent;

        private const long MinUiTicks = 40 * 10000L;
        private const long FullListPaintMinTicks = 180 * 10000L;
        private const long RssiHideAfterTicks = 2 * 10000000L;
        private const long StreamStaleTicks = 450 * 10000L;
        private const long MinRecordTicks = 100 * 10000L;

        public RssiTrackForm(AuditConfig cfg)
        {
            _cfg = cfg ?? new AuditConfig();
            _client = new AuditClient(_cfg);
            UiTheme.ApplyForm(this);
            Text = "RSSI track";
            Width = 240;
            Height = 320;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            _nur = new NurApiBridge(this, _cfg);
            _nur.TagReadingsReady += OnNurTagReadings;
            _geiger = new RssiGeigerAudio();

            _status = UiTheme.MakeHeader("trigger=scan · 2=track · 8=RF preset");
            CfLayout.Place(_status, 6, 4, 228, 18);

            _rfProfile = new Label
            {
                ForeColor = UiTheme.Muted,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Text = "RF: …",
            };
            CfLayout.Place(_rfProfile, 6, 22, 228, 14);

            _target = new Label
            {
                ForeColor = UiTheme.Muted,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Text = "Target: (none) — pick from list",
            };
            CfLayout.Place(_target, 6, 36, 228, 22);

            _tagList = new ListBox
            {
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                BackColor = UiTheme.Card,
                ForeColor = UiTheme.Text,
            };
            CfLayout.Place(_tagList, 6, 58, 228, 100);
            _tagList.SelectedIndexChanged += delegate { UpdateTargetFromSelection(false); };

            _trackPanel = new Panel { BackColor = UiTheme.Bg, Visible = false };
            CfLayout.Place(_trackPanel, 6, 58, 228, 100);

            _signalBanner = new Label
            {
                ForeColor = UiTheme.Muted,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                Text = "—",
                TextAlign = ContentAlignment.TopCenter,
                Parent = _trackPanel,
            };
            CfLayout.Place(_signalBanner, 0, 0, 228, 16);

            _lastSeenLabel = new Label
            {
                ForeColor = UiTheme.Muted,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Text = "",
                TextAlign = ContentAlignment.TopCenter,
                Parent = _trackPanel,
            };
            CfLayout.Place(_lastSeenLabel, 0, 16, 228, 12);

            _rssiBig = new Label
            {
                ForeColor = UiTheme.Good,
                Font = new Font("Tahoma", 12f, FontStyle.Bold),
                Text = "---",
                Parent = _trackPanel,
            };
            CfLayout.Place(_rssiBig, 0, 30, 228, 24);

            _barTrack = new Panel { BackColor = Color.FromArgb(45, 55, 72), Parent = _trackPanel };
            CfLayout.Place(_barTrack, 0, 56, 228, 14);

            _barFill = new Panel
            {
                BackColor = Color.FromArgb(180, 48, 48),
                Height = 14,
                Width = 0,
                Left = 0,
                Top = 0,
            };
            _barTrack.Controls.Add(_barFill);

            _closeLabel = new Label
            {
                ForeColor = UiTheme.Muted,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Text = "far",
                TextAlign = ContentAlignment.TopCenter,
                Parent = _trackPanel,
            };
            CfLayout.Place(_closeLabel, 0, 72, 228, 14);

            var trackHint = UiTheme.MakeHint("5=list · hold trigger · target only");
            trackHint.Parent = _trackPanel;
            CfLayout.Place(trackHint, 0, 88, 228, 14);

            _rec = new Label
            {
                ForeColor = UiTheme.Text,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Text = "Rec off · on gun until Save(4)",
            };
            CfLayout.Place(_rec, 6, 168, 228, 28);

            var hint = UiTheme.MakeHint("6-9=rows ···=tag suffix");
            CfLayout.Place(hint, 6, 198, 228, 12);

            _btnScan = UiTheme.MakeHotkeyButton(1, "Clear", true);
            CfLayout.Place(_btnScan, 6, 264, 72, 30);
            _btnScan.Click += delegate { ClearTagList(); };

            _btnTrack = UiTheme.MakeHotkeyButton(2, "Track", true);
            CfLayout.Place(_btnTrack, 84, 264, 72, 30);
            _btnTrack.Click += delegate { ToggleTrack(); };

            var btnRec = UiTheme.MakeHotkeyButton(3, "Rec", false);
            CfLayout.Place(btnRec, 162, 264, 36, 30);
            btnRec.Click += delegate { ToggleRecording(); };

            var btnSave = UiTheme.MakeHotkeyButton(4, "Save", false);
            CfLayout.Place(btnSave, 200, 264, 34, 30);
            btnSave.Click += delegate { StopRecordingAndUpload(); };

            Controls.Add(btnSave);
            Controls.Add(btnRec);
            Controls.Add(_btnTrack);
            Controls.Add(_btnScan);
            Controls.Add(hint);
            Controls.Add(_rec);
            Controls.Add(_trackPanel);
            Controls.Add(_tagList);
            Controls.Add(_target);
            Controls.Add(_rfProfile);
            Controls.Add(_status);

            _recTimer = new System.Windows.Forms.Timer();
            _recTimer.Interval = 900;
            _recTimer.Tick += delegate { RefreshRecLabel(); };

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 150;
            _uiTimer.Tick += UiTimer_Tick;

            KeyDown += RssiTrackForm_KeyDown;
            KeyUp += RssiTrackForm_KeyUp;
            Load += delegate
            {
                _nur.Start();
                if (_nur.IsAvailable)
                {
                    _nur.ApplyRfPreset(NurRfPresets.Get(_cfg.RfPresetIndex));
                }
                SetPickMode();
                RefreshRfProfileLabel();
                RefreshTrackButtonLabel();
                _status.Text = "Pull trigger to scan";
                _recTimer.Enabled = true;
                _uiTimer.Enabled = true;
                TestSessionLog.Add("rssi_track", "opened", _nur.Status);
            };
            Closed += delegate
            {
                _inTrackView = false;
                _trackActive = false;
                EndTriggerHold();
                _geiger.Dispose();
                _uiTimer.Enabled = false;
                _recTimer.Enabled = false;
                if (RssiTraceRecorder.IsRecording) RssiTraceRecorder.StopRecording();
                _nur.TagReadingsReady -= OnNurTagReadings;
                _nur.Dispose();
            };
        }

        private void RssiTrackForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1 || e.KeyCode == Keys.F9)
            {
                OnTriggerDown();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { Close(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) { ClearTagList(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) { ToggleTrack(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) { ToggleRecording(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4) { StopRecordingAndUpload(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D5 || e.KeyCode == Keys.NumPad5) { SetPickMode(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D8 || e.KeyCode == Keys.NumPad8) { CycleRfPreset(); e.Handled = true; return; }

            int pick = KeyToPickIndex(e.KeyCode);
            if (pick >= 0)
            {
                SelectListIndex(pick);
                UpdateTargetFromSelection(false);
                e.Handled = true;
            }
        }

        private void RssiTrackForm_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1 || e.KeyCode == Keys.F9)
            {
                EndTriggerHold();
                e.Handled = true;
            }
        }

        private static int KeyToPickIndex(Keys key)
        {
            if (key == Keys.D6 || key == Keys.NumPad6) return 0;
            if (key == Keys.D7 || key == Keys.NumPad7) return 1;
            if (key == Keys.D8 || key == Keys.NumPad8) return 2;
            if (key == Keys.D9 || key == Keys.NumPad9) return 3;
            return -1;
        }

        private void SetPickMode()
        {
            _inTrackView = false;
            _trackActive = false;
            _geiger.SetEnabled(false);
            _geiger.Stop();
            _tagList.Visible = true;
            _trackPanel.Visible = false;
            RefreshStatusLine();
            RefreshTrackButtonLabel();
        }

        private void EnterTrackView()
        {
            _inTrackView = true;
            _trackActive = false;
            _lastUiTicks = 0;
            _lastRecordTicks = 0;
            _lastPaintedTrackRssi = -999;
            _holdHasRssi = false;
            _lastSeenTicks = 0;
            _signalLive = false;
            _hadLiveSignal = false;
            _lostCueSent = false;
            _geiger.SetEnabled(false);
            _geiger.Stop();
            SetTrackMode();
            PaintSignalIdle();
            RefreshStatusLine();
            RefreshTrackButtonLabel();
        }

        private void OnTriggerDown()
        {
            if (!_nur.IsAvailable)
            {
                _status.Text = _nur.Status;
                return;
            }
            if (!_triggerHeld)
            {
                _triggerHeld = true;
                _nur.EnsureInventoryStream();
            }
            if (_inTrackView && _trackActive)
            {
                _hadLiveSignal = false;
                _signalLive = false;
                _lastSeenTicks = 0;
                _lostCueSent = true;
                _geiger.Stop();
                PaintSignalSearching();
            }
            PulseWhileHeld();
            RefreshStatusLine();
        }

        private void EndTriggerHold()
        {
            if (!_triggerHeld) return;
            _triggerHeld = false;
            _nur.StopInventoryStreamSafe();
            _geiger.UpdateLiveSignal(false, 0);
            RefreshStatusLine();
            if (_inTrackView && _trackActive)
            {
                _closeLabel.Text = "pull trigger";
            }
        }

        private void PulseWhileHeld()
        {
            if (!_triggerHeld) return;
            if (_inTrackView && _trackActive)
            {
                _nur.TriggerFreshRound(RssiTraceRecorder.TargetEpc);
            }
            else
            {
                _nur.TriggerInventory();
            }
        }

        private void ClearTagList()
        {
            _listEntries = new RssiListEntry[0];
            _listOrderDirty = false;
            _tagList.Items.Clear();
            _target.Text = "Target: (none) — pick from list";
            RssiTraceRecorder.SetTarget("");
            _status.Text = "List cleared";
        }

        private void ToggleTrack()
        {
            if (!_inTrackView)
            {
                if (_listEntries.Length == 0)
                {
                    _status.Text = "Pull trigger to scan first";
                    return;
                }
                if (_tagList.SelectedIndex < 0 && _listEntries.Length > 0)
                {
                    _tagList.SelectedIndex = 0;
                }
                UpdateTargetFromSelection(false);
                if (RssiTraceRecorder.TargetEpc.Length == 0)
                {
                    _status.Text = "Select a tag from list";
                    return;
                }
                EnterTrackView();
                return;
            }

            _trackActive = !_trackActive;
            if (_trackActive)
            {
                _holdHasRssi = false;
                _lastPaintedTrackRssi = -999;
                _lastSeenTicks = 0;
                _signalLive = false;
                _hadLiveSignal = false;
                _lostCueSent = false;
                _geiger.SetEnabled(true);
                PaintSignalIdle();
                if (_triggerHeld) _nur.TriggerFreshRound(RssiTraceRecorder.TargetEpc);
            }
            else
            {
                _geiger.SetEnabled(false);
                _geiger.Stop();
                PaintSignalIdle();
            }
            RefreshTrackButtonLabel();
            RefreshStatusLine();
        }

        private void CycleRfPreset()
        {
            if (!_nur.IsAvailable) return;
            _cfg.RfPresetIndex = NurRfPresets.NormalizeIndex(_cfg.RfPresetIndex + 1);
            _cfg.Save();
            NurRfPreset preset = NurRfPresets.Get(_cfg.RfPresetIndex);
            _nur.ApplyRfPreset(preset);
            RefreshRfProfileLabel(preset);
            _status.Text = "RF preset: " + preset.ShortLabel() + " (8=next)";
            TestSessionLog.Add("rf_preset", preset.Id, preset.Label);
        }

        private void RefreshRfProfileLabel()
        {
            RefreshRfProfileLabel(NurRfPresets.Get(_cfg.RfPresetIndex));
        }

        private void RefreshRfProfileLabel(NurRfPreset preset)
        {
            NurProfileStatus p = _nur.ReaderProfile;
            string line = preset != null ? (preset.ShortLabel() + " · ") : "";
            _rfProfile.Text = line + p.ToDisplayLine();
            if (p.ApplyOk && p.LinkFreqOk && p.TxLevelOk)
            {
                _rfProfile.ForeColor = UiTheme.Good;
            }
            else if (p.ApplyOk)
            {
                _rfProfile.ForeColor = UiTheme.Warn;
            }
            else
            {
                _rfProfile.ForeColor = UiTheme.Muted;
            }
        }

        private void RefreshTrackButtonLabel()
        {
            if (!_inTrackView)
            {
                _btnTrack.Text = "2  Track";
            }
            else
            {
                _btnTrack.Text = _trackActive ? "2  Track OFF" : "2  Track ON";
            }
        }

        private void RefreshStatusLine()
        {
            if (_inTrackView)
            {
                string tail = EpcDisplay.Suffix(RssiTraceRecorder.TargetEpc, 6);
                if (_triggerHeld && _trackActive)
                {
                    _status.Text = "Track ON · scanning · " + tail;
                }
                else if (_trackActive)
                {
                    _status.Text = "Track ON · pull trigger · " + tail;
                }
                else
                {
                    _status.Text = "Track view · press 2 ON · " + tail;
                }
                return;
            }

            if (_triggerHeld)
            {
                _status.Text = "Scanning…";
            }
            else
            {
                _status.Text = "Pull trigger to scan";
            }
        }

        private void SetTrackMode()
        {
            _tagList.Visible = false;
            _trackPanel.Visible = true;
            _trackPanel.BringToFront();
        }

        private void OnNurTagReadings(object sender, NurTagReadingsEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { OnNurTagReadings(sender, e); }), null, EventArgs.Empty);
                return;
            }
            if (e == null || !_triggerHeld) return;

            if (_inTrackView)
            {
                if (!_trackActive) return;

                string target = RssiTraceRecorder.TargetEpc;
                NurTagReading[] trackRound = _nur.FetchTrackRoundTags(target);

                if (RssiTraceRecorder.IsRecording)
                {
                    RssiTraceRecorder.AddScanFromReadings(trackRound);
                }
                UpdateFromReadings(trackRound, "stream");
                return;
            }

            NurTagReading[] readings = e.Readings;
            if (readings == null || readings.Length == 0) return;

            if (RssiTraceRecorder.IsRecording)
            {
                RssiTraceRecorder.AddScanFromReadings(readings);
            }
            MergeTagListData(readings, false);
        }

        private void UiTimer_Tick(object sender, EventArgs e)
        {
            long now = DateTime.UtcNow.Ticks;

            if (_triggerHeld)
            {
                bool stale = !_nur.IsStreamRunning
                    || (now - _nur.LastEmitTicks) >= StreamStaleTicks;
                if (stale)
                {
                    _nur.EnsureInventoryStream();
                    PulseWhileHeld();
                }
            }

            if (_inTrackView)
            {
                RefreshTrackSignalDisplay(now);
                if (!_triggerHeld && _trackActive)
                {
                    _closeLabel.Text = "pull trigger";
                }
                return;
            }

            if (!_triggerHeld) return;
            if (_listEntries.Length == 0) return;
            if ((now - _lastFullListPaintTicks) >= FullListPaintMinTicks)
            {
                PaintTagListIncremental(false);
            }
        }

        private void MergeTagListData(NurTagReading[] readings, bool replaceOnly)
        {
            int rawCount = readings == null ? 0 : readings.Length;
            if (rawCount == 0) return;

            int oldCount = _listEntries.Length;
            RssiListEntry[] incoming = RssiTagList.FromReadings(readings);
            if (replaceOnly || _listEntries.Length == 0)
            {
                _listEntries = RssiTagList.SortAndCap(incoming, RssiTagList.DefaultAccumulateMax);
                _listOrderDirty = true;
            }
            else
            {
                _listEntries = RssiTagList.MergeLists(
                    _listEntries, incoming, RssiTagList.DefaultAccumulateMax);
                if (_listEntries.Length != oldCount) _listOrderDirty = true;
            }

            UpdateSelectedRssiLabel();

            long now = DateTime.UtcNow.Ticks;
            bool needFull = replaceOnly || _listOrderDirty
                || (now - _lastFullListPaintTicks) >= FullListPaintMinTicks;
            if (needFull)
            {
                PaintTagListIncremental(replaceOnly);
            }
            else
            {
                PaintTagListRssiOnly();
            }
        }

        private void PaintTagListIncremental(bool forceReselect)
        {
            string keepEpc = "";
            int keepIdx = _tagList.SelectedIndex;
            if (!forceReselect && keepIdx >= 0 && keepIdx < _listEntries.Length)
            {
                keepEpc = _listEntries[keepIdx].Epc;
            }

            int newSel = 0;
            for (int i = 0; i < _listEntries.Length; i++)
            {
                string line = (i + 1) + ". " + _listEntries[i].DisplayLine;
                if (i < _tagList.Items.Count)
                {
                    if ((string)_tagList.Items[i] != line) _tagList.Items[i] = line;
                }
                else
                {
                    _tagList.Items.Add(line);
                }
                if (keepEpc.Length > 0 && _listEntries[i].Epc == keepEpc) newSel = i;
            }
            while (_tagList.Items.Count > _listEntries.Length)
            {
                _tagList.Items.RemoveAt(_tagList.Items.Count - 1);
            }

            _status.Text = _listEntries.Length + " tags";
            if (_listEntries.Length > 0)
            {
                _tagList.SelectedIndex = newSel;
            }
            _listOrderDirty = false;
            _lastFullListPaintTicks = DateTime.UtcNow.Ticks;
            UpdateSelectedRssiLabel();
            RefreshRecLabel();
        }

        private void PaintTagListRssiOnly()
        {
            int n = _listEntries.Length;
            if (n > _tagList.Items.Count) n = _tagList.Items.Count;
            for (int i = 0; i < n; i++)
            {
                string line = (i + 1) + ". " + _listEntries[i].DisplayLine;
                if ((string)_tagList.Items[i] != line) _tagList.Items[i] = line;
            }
            UpdateSelectedRssiLabel();
        }

        private void UpdateSelectedRssiLabel()
        {
            int idx = _tagList.SelectedIndex;
            if (idx < 0 || idx >= _listEntries.Length) return;
            RssiListEntry entry = _listEntries[idx];
            if (!entry.HasRssi) return;
            _target.Text = "Sel: " + EpcDisplay.ListLine(entry.Rssi, true, entry.Epc);
        }

        private void SelectListIndex(int index)
        {
            if (index < 0 || index >= _tagList.Items.Count) return;
            _tagList.SelectedIndex = index;
        }

        private void UpdateTargetFromSelection(bool startTrack)
        {
            int idx = _tagList.SelectedIndex;
            if (idx < 0 || idx >= _listEntries.Length) return;

            RssiListEntry entry = _listEntries[idx];
            RssiTraceRecorder.SetTarget(entry.Epc);
            _target.Text = "Target: " + EpcDisplay.TargetLabel(entry.Epc);
            TestSessionLog.Add("rssi_track", "target", entry.Epc);

            if (startTrack) ToggleTrack();
        }

        private void ToggleRecording()
        {
            if (RssiTraceRecorder.IsRecording)
            {
                RssiTraceRecorder.PauseRecording();
                RefreshRecLabel();
                _status.Text = "Recording paused";
                return;
            }

            if (!RssiTraceRecorder.HasSessionData)
            {
                RssiTraceRecorder.StartRecording();
                AuditPendingQueue.MarkRssiTrace();
            }
            else
            {
                RssiTraceRecorder.ResumeRecording();
            }

            RefreshRecLabel();
            _status.Text = "Recording ON — on gun, Save(4)=upload";
        }

        private void RefreshRecLabel()
        {
            if (!RssiTraceRecorder.IsRecording)
            {
                if (RssiTraceRecorder.HasSessionData)
                {
                    _rec.Text = "Paused tr=" + RssiTraceRecorder.TrackSampleCount
                        + " sc=" + RssiTraceRecorder.ScanSnapshotCount
                        + " · on gun";
                }
                else
                {
                    _rec.Text = "Rec off · on gun until Save(4)";
                }
                _rec.ForeColor = UiTheme.Text;
                return;
            }

            long sec = RssiTraceRecorder.SessionElapsedMs / 1000L;
            long idle = RssiTraceRecorder.IdleMs / 1000L;
            _rec.ForeColor = idle > 3 ? UiTheme.Warn : UiTheme.Good;
            _rec.Text = "● " + sec + "s tr=" + RssiTraceRecorder.TrackSampleCount
                + " sc=" + RssiTraceRecorder.ScanSnapshotCount;
            if (idle >= 2)
            {
                _rec.Text += " · idle " + idle + "s";
            }
            _rec.Text += " · local";
        }

        private static NurTagReading FindLatestMatch(NurTagReading[] readings, string target)
        {
            if (readings == null || target.Length == 0) return null;
            NurTagReading latest = null;
            string normTarget = RssiTraceRecorder.NormalizeEpc(target);
            string tail6 = normTarget.Length >= 6
                ? normTarget.Substring(normTarget.Length - 6) : normTarget;
            string tail8 = normTarget.Length >= 8
                ? normTarget.Substring(normTarget.Length - 8) : normTarget;

            for (int i = 0; i < readings.Length; i++)
            {
                string e = RssiTraceRecorder.NormalizeEpc(readings[i].Epc);
                bool match = e == normTarget;
                if (!match && tail8.Length >= 6 && e.Length >= tail8.Length)
                {
                    match = e.Substring(e.Length - tail8.Length) == tail8;
                }
                if (!match && tail6.Length >= 4 && e.Length >= tail6.Length)
                {
                    match = e.Substring(e.Length - tail6.Length) == tail6;
                }
                if (!match) continue;
                latest = readings[i];
            }
            return latest;
        }

        private void StopRecordingAndUpload()
        {
            if (!RssiTraceRecorder.HasSessionData && !RssiTraceRecorder.IsRecording)
            {
                _status.Text = "Nothing to save";
                return;
            }

            if (RssiTraceRecorder.IsRecording) RssiTraceRecorder.StopRecording();
            _rec.Text = "Uploading to server…";
            string json = RssiTraceRecorder.ToJson();
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _client.UploadRssiTrace(json);
                BeginInvoke(new EventHandler(delegate
                {
                    if (res.Ok)
                    {
                        RssiTraceRecorder.ClearPending();
                        _status.Text = "Trace on server";
                        _rec.Text = "Saved tr=" + RssiTraceRecorder.TrackSampleCount
                            + " scans=" + RssiTraceRecorder.ScanSnapshotCount;
                        _rec.ForeColor = UiTheme.Text;
                    }
                    else
                    {
                        AuditPendingQueue.MarkRssiTrace();
                        _status.Text = "Upload failed — on gun";
                        _rec.Text = "Pending upload";
                    }
                }), null, EventArgs.Empty);
            });
        }

        private void UpdateFromReadings(NurTagReading[] readings, string source)
        {
            string target = RssiTraceRecorder.TargetEpc;
            if (target.Length == 0) return;

            NurTagReading match = null;
            if (readings != null && readings.Length > 0)
            {
                match = readings[0];
            }
            bool seen = match != null;
            int rssi = seen && match.HasRssi ? match.Rssi : 0;
            bool has = seen && match.HasRssi;
            long now = DateTime.UtcNow.Ticks;

            if (RssiTraceRecorder.IsRecording && (now - _lastRecordTicks) >= MinRecordTicks)
            {
                _lastRecordTicks = now;
                RssiTraceRecorder.AddReading(rssi, has, seen);
                if (!seen)
                {
                    RssiTraceRecorder.AddDiag("track_miss", source
                        + " epcSel=" + (_nur.ReaderProfile.LastEpcSelectOk ? "1" : "0"));
                }
            }

            if (seen && has)
            {
                if (!_hadLiveSignal)
                {
                    _geiger.NotifyFound();
                }
                _hadLiveSignal = true;
                _lostCueSent = false;
                _signalLive = true;
                _lastSeenTicks = now;
                _holdRssi = rssi;
                _holdHasRssi = true;
                _holdRssiTicks = now;

                PaintSignalFound();
                if (rssi != _lastPaintedTrackRssi || (now - _lastUiTicks) >= MinUiTicks)
                {
                    _lastUiTicks = now;
                    _lastPaintedTrackRssi = rssi;
                    PaintTrackRssi(rssi, false);
                    if (RssiTraceRecorder.IsRecording)
                    {
                        RssiTraceRecorder.AddDiag("track_ok", rssi + " " + EpcDisplay.Suffix(target, 6));
                    }
                }
                _geiger.UpdateLiveSignal(_trackActive && _triggerHeld, rssi);
                RefreshRecLabel();
                _status.Text = "FOUND " + source;
                return;
            }

            _signalLive = false;
            _geiger.UpdateLiveSignal(false, 0);
            RefreshTrackSignalDisplay(now);

            bool canRefreshUi = (now - _lastUiTicks) >= MinUiTicks;
            if (!canRefreshUi) return;
            _lastUiTicks = now;

            RefreshRecLabel();
            _status.Text = (_hadLiveSignal ? "LOST" : "--") + " " + source;
        }

        private void RefreshTrackSignalDisplay(long now)
        {
            if (!_inTrackView || !_trackActive)
            {
                return;
            }

            if (_signalLive)
            {
                return;
            }

            if (_lastSeenTicks <= 0)
            {
                PaintSignalSearching();
                return;
            }

            long ageTicks = now - _lastSeenTicks;
            long ageSec = ageTicks / 10000000L;
            if (ageSec < 0) ageSec = 0;

            if (ageTicks < RssiHideAfterTicks)
            {
                PaintSignalFading(ageSec);
                if (_holdHasRssi)
                {
                    PaintTrackRssi(_holdRssi, true);
                }
                return;
            }

            if (_hadLiveSignal && !_lostCueSent)
            {
                _geiger.NotifyLost();
                _lostCueSent = true;
            }

            PaintSignalLost(ageSec);
            _rssiBig.Text = "---";
            _rssiBig.ForeColor = UiTheme.Muted;
            ClearProximityBar();
            _closeLabel.Text = "sweep again";
            _closeLabel.ForeColor = UiTheme.Muted;
        }

        private void PaintSignalIdle()
        {
            _signalBanner.Text = _trackActive ? "READY" : "TRACK OFF";
            _signalBanner.ForeColor = UiTheme.Muted;
            _signalBanner.BackColor = UiTheme.Bg;
            _lastSeenLabel.Text = _trackActive ? "hold trigger" : "";
            _rssiBig.Text = "---";
            _rssiBig.ForeColor = UiTheme.Muted;
            ClearProximityBar();
            _closeLabel.Text = _trackActive ? "pull trigger" : "press 2 ON";
            _closeLabel.ForeColor = UiTheme.Muted;
        }

        private void PaintSignalSearching()
        {
            _signalBanner.Text = "SEARCHING";
            _signalBanner.ForeColor = UiTheme.Muted;
            _signalBanner.BackColor = UiTheme.Bg;
            _lastSeenLabel.Text = "not found yet";
        }

        private void PaintSignalFound()
        {
            _signalBanner.Text = "● FOUND";
            _signalBanner.ForeColor = Color.FromArgb(120, 255, 120);
            _signalBanner.BackColor = Color.FromArgb(20, 60, 30);
            _lastSeenLabel.Text = "live signal";
        }

        private void PaintSignalFading(long ageSec)
        {
            _signalBanner.Text = "○ FADING";
            _signalBanner.ForeColor = UiTheme.Warn;
            _signalBanner.BackColor = Color.FromArgb(55, 45, 20);
            _lastSeenLabel.Text = "last seen " + ageSec + "s ago";
        }

        private void PaintSignalLost(long ageSec)
        {
            _signalBanner.Text = "○ LOST";
            _signalBanner.ForeColor = Color.FromArgb(255, 170, 90);
            _signalBanner.BackColor = Color.FromArgb(55, 30, 20);
            _lastSeenLabel.Text = "last seen " + ageSec + "s ago";
        }

        private void PaintTrackRssi(int rssi, bool held)
        {
            _rssiBig.Text = (held ? "~" : "") + rssi + " dBm";
            _rssiBig.ForeColor = RssiProximity.ColorForRssi(rssi);
            UpdateProximityBar(rssi);
            _closeLabel.Text = RssiProximity.ClosenessLabel(rssi);
            _closeLabel.ForeColor = RssiProximity.ColorForRssi(rssi);
        }

        private void UpdateProximityBar(int rssiDbm)
        {
            int trackW = _barTrack.ClientSize.Width;
            if (trackW < 8) trackW = 220;
            int pct = RssiProximity.Percent(rssiDbm);
            int fillW = (trackW * pct) / 100;
            if (pct > 0 && fillW < 4) fillW = 4;
            if (fillW > trackW) fillW = trackW;

            _barFill.BackColor = RssiProximity.ColorForRssi(rssiDbm);
            _barFill.Width = fillW;
            _barFill.Height = _barTrack.ClientSize.Height > 0 ? _barTrack.ClientSize.Height : 14;
        }

        private void ClearProximityBar()
        {
            _lastPaintedTrackRssi = -999;
            _barFill.Width = 0;
            _barFill.BackColor = Color.FromArgb(80, 80, 80);
        }

    }
}
