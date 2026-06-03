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
        private readonly Label _target;
        private readonly Label _rssiBig;
        private readonly Label _bar;
        private readonly Label _rec;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private bool _tracking;
        private int _lastRssi = -999;
        private bool _lastHasRssi;

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
            _nur.TagReadingsReady += NurOnReadings;

            _status = UiTheme.MakeHeader("1=scan tag 2=track 0=back");
            CfLayout.Place(_status, 6, 4, 228, 18);

            _target = new Label
            {
                ForeColor = UiTheme.Muted,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Text = "Target: (none)",
            };
            CfLayout.Place(_target, 6, 24, 228, 28);

            _rssiBig = new Label
            {
                ForeColor = UiTheme.Good,
                Font = new Font("Tahoma", 14f, FontStyle.Bold),
                Text = "---",
            };
            CfLayout.Place(_rssiBig, 6, 54, 228, 28);

            _bar = new Label
            {
                ForeColor = UiTheme.Accent,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                Text = "----------",
            };
            CfLayout.Place(_bar, 6, 84, 228, 18);

            _rec = new Label
            {
                ForeColor = UiTheme.Text,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Text = "Recording: off",
            };
            CfLayout.Place(_rec, 6, 106, 228, 32);

            var hint = UiTheme.MakeHint("3=start rec 4=stop+upload");
            CfLayout.Place(hint, 6, 248, 228, 14);

            var btnScan = UiTheme.MakeHotkeyButton(1, "Scan tag", true);
            CfLayout.Place(btnScan, 6, 264, 110, 30);
            btnScan.Click += delegate { CaptureTarget(); };

            var btnTrack = UiTheme.MakeHotkeyButton(2, "Track", true);
            CfLayout.Place(btnTrack, 124, 264, 110, 30);
            btnTrack.Click += delegate { StartTracking(); };

            Controls.Add(btnTrack);
            Controls.Add(btnScan);
            Controls.Add(hint);
            Controls.Add(_rec);
            Controls.Add(_bar);
            Controls.Add(_rssiBig);
            Controls.Add(_target);
            Controls.Add(_status);

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = 450;
            _pollTimer.Tick += delegate { PollTags(); };

            KeyDown += RssiTrackForm_KeyDown;
            Load += delegate
            {
                _nur.Start();
                _nur.EnsureInventoryStream();
                _status.Text = _nur.Status;
                TestSessionLog.Add("rssi_track", "opened", _nur.Status);
            };
            Closed += delegate
            {
                _pollTimer.Enabled = false;
                if (RssiTraceRecorder.IsRecording) RssiTraceRecorder.StopRecording();
                _nur.TagReadingsReady -= NurOnReadings;
                _nur.Dispose();
            };
        }

        private void RssiTrackForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { Close(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) { CaptureTarget(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) { StartTracking(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) { StartRecording(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4) { StopRecordingAndUpload(); e.Handled = true; }
        }

        private void CaptureTarget()
        {
            NurTagReading[] readings = _nur.ReadTagsNow();
            string epc = PickBestEpc(readings);
            if (epc.Length == 0)
            {
                _status.Text = "No tag — pull trigger";
                return;
            }
            RssiTraceRecorder.SetTarget(epc);
            _target.Text = "Target: " + ShortEpc(epc);
            _status.Text = "Tag selected";
            TestSessionLog.Add("rssi_track", "target", epc);
        }

        private void StartTracking()
        {
            if (RssiTraceRecorder.TargetEpc.Length == 0)
            {
                _status.Text = "Scan tag first (key 1)";
                return;
            }
            _tracking = true;
            _pollTimer.Enabled = true;
            _nur.EnsureInventoryStream();
            _status.Text = "Tracking… pull trigger";
            PollTags();
        }

        private void StartRecording()
        {
            if (RssiTraceRecorder.TargetEpc.Length == 0)
            {
                _status.Text = "Select tag first";
                return;
            }
            RssiTraceRecorder.StartRecording();
            AuditPendingQueue.MarkRssiTrace();
            _rec.Text = "Recording: ON";
            _status.Text = "Recording RSSI";
        }

        private void StopRecordingAndUpload()
        {
            if (!RssiTraceRecorder.IsRecording)
            {
                _status.Text = "Not recording";
                return;
            }
            RssiTraceRecorder.StopRecording();
            _rec.Text = "Recording: off  uploading…";
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
                        _rec.Text = "Uploaded " + RssiTraceRecorder.SampleCount + " samples";
                    }
                    else
                    {
                        AuditPendingQueue.MarkRssiTrace();
                        _status.Text = "Upload failed — saved on gun";
                        _rec.Text = "Pending upload";
                    }
                }), null, EventArgs.Empty);
            });
        }

        private void PollTags()
        {
            if (!_tracking) return;
            NurTagReading[] readings = _nur.ReadTagsNow();
            UpdateFromReadings(readings, "poll");
        }

        private void NurOnReadings(object sender, NurTagReadingsEventArgs e)
        {
            if (!_tracking) return;
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { NurOnReadings(sender, e); }), null, EventArgs.Empty);
                return;
            }
            UpdateFromReadings(e.Readings, "event");
        }

        private void UpdateFromReadings(NurTagReading[] readings, string source)
        {
            string target = RssiTraceRecorder.TargetEpc;
            if (target.Length == 0) return;

            NurTagReading match = FindMatch(readings, target);
            bool seen = match != null;
            int rssi = seen && match.HasRssi ? match.Rssi : 0;
            bool has = seen && match.HasRssi;

            if (seen && has) _lastRssi = rssi;
            if (seen) _lastHasRssi = has;

            if (seen && has)
            {
                _rssiBig.Text = rssi + " dBm";
                _bar.Text = BarForRssi(rssi);
            }
            else if (seen)
            {
                _rssiBig.Text = "seen (no RSSI)";
                _bar.Text = "----------";
            }
            else
            {
                _rssiBig.Text = "not in field";
                _bar.Text = "----------";
            }

            if (RssiTraceRecorder.IsRecording)
            {
                RssiTraceRecorder.AddReading(rssi, has, seen);
                _rec.Text = "Rec: " + RssiTraceRecorder.SampleCount + " samples";
            }

            _status.Text = (seen ? "OK" : "--") + " " + source;
        }

        private static NurTagReading FindMatch(NurTagReading[] readings, string target)
        {
            if (readings == null || target.Length == 0) return null;
            for (int i = 0; i < readings.Length; i++)
            {
                string e = RssiTraceRecorder.NormalizeEpc(readings[i].Epc);
                if (e == target) return readings[i];
                if (e.Length >= 12 && target.Length >= 12
                    && e.StartsWith(target.Substring(0, 12))) return readings[i];
            }
            return null;
        }

        private static string PickBestEpc(NurTagReading[] readings)
        {
            if (readings == null || readings.Length == 0) return "";
            NurTagReading best = readings[0];
            for (int i = 1; i < readings.Length; i++)
            {
                if (readings[i].HasRssi && readings[i].Rssi > best.Rssi)
                {
                    best = readings[i];
                }
            }
            return RssiTraceRecorder.NormalizeEpc(best.Epc);
        }

        private static string BarForRssi(int rssi)
        {
            var r = new NurTagReading();
            r.Rssi = rssi;
            r.HasRssi = true;
            return r.StrengthBar;
        }

        private static string ShortEpc(string epc)
        {
            if (epc == null || epc.Length <= 20) return epc ?? "";
            return epc.Substring(0, 19) + "~";
        }
    }
}
