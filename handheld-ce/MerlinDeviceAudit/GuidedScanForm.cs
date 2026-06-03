using System;
using System.Collections;
using System.Drawing;
using System.Windows.Forms;

namespace MerlinAudit
{
    /// <summary>
    /// Walks the operator through trigger RFID and Scan-key barcode reads.
    /// RFID uses NUR API; barcodes use keyboard wedge capture.
    /// </summary>
    internal sealed class GuidedScanForm : Form
    {
        private readonly AuditConfig _cfg;
        private readonly WedgeInputCapture _capture = new WedgeInputCapture();
        private readonly NurApiBridge _nur;
        private readonly System.Windows.Forms.Timer _focusTimer;
        private readonly ArrayList _entries = new ArrayList();
        private readonly Label _instruction;
        private readonly Label _detail;
        private readonly Label _lastRead;
        private readonly Button _skipBtn;
        private readonly Button _cancelBtn;
        private int _stepIndex;
        private bool _finished;
        private bool _closed;
        private long _lastNurEmitTicks;

        private static readonly string[][] Steps = new string[][]
        {
            new string[] { "rfid", "Pull TRIGGER — RFID 1 of 3" },
            new string[] { "rfid", "Pull TRIGGER — RFID 2 of 3" },
            new string[] { "rfid", "Pull TRIGGER — RFID 3 of 3" },
            new string[] { "barcode", "Press SCAN — barcode 1 of 3" },
            new string[] { "barcode", "Press SCAN — barcode 2 of 3" },
            new string[] { "barcode", "Press SCAN — barcode 3 of 3" },
            new string[] { "any", "Scan anything (no tap) 1 of 1" },
        };

        public GuidedScanForm(AuditConfig cfg)
        {
            _cfg = cfg ?? new AuditConfig();
            _nur = new NurApiBridge(this, _cfg);

            Text = "Scan guide";
            Width = 240;
            Height = 280;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(15, 23, 42);
            ForeColor = Color.White;
            Font = new Font("Tahoma", 8f, FontStyle.Regular);
            KeyPreview = true;

            _instruction = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                ForeColor = Color.FromArgb(56, 189, 248),
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                Text = "Starting…",
            };

            _detail = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                ForeColor = Color.FromArgb(148, 163, 184),
                Text = "Loading hardware…",
            };

            _lastRead = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(203, 213, 225),
                Text = "Waiting for scan…",
            };

            var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            _cancelBtn = new Button
            {
                Text = "Cancel",
                Width = 110,
                Height = 24,
                Left = 4,
                Top = 2,
            };
            _skipBtn = new Button
            {
                Text = "Skip step",
                Width = 110,
                Height = 24,
                Left = 118,
                Top = 2,
            };
            _cancelBtn.Click += delegate { Finish(false); };
            _skipBtn.Click += delegate { AdvanceStep(null, "skipped"); };
            btnRow.Controls.Add(_skipBtn);
            btnRow.Controls.Add(_cancelBtn);

            Controls.Add(_lastRead);
            Controls.Add(btnRow);
            Controls.Add(_detail);
            Controls.Add(_instruction);
            Controls.Add(_capture);
            _capture.BringToFront();

            _capture.LineReceived += CaptureOnLineReceived;
            _capture.Activity += delegate
            {
                if (_closed) return;
                _lastRead.Text = "Wedge: " + _capture.BufferLength + " chars (auto OK)";
            };
            _nur.TagsInventoryReady += NurOnTagsReady;

            _focusTimer = new System.Windows.Forms.Timer();
            _focusTimer.Interval = 450;
            _focusTimer.Tick += delegate
            {
                if (_closed) return;
                if (!_capture.Focused) _capture.ArmCapture();
            };

            KeyPress += GuidedScanForm_KeyPress;
            KeyDown += GuidedScanForm_KeyDown;

            Load += delegate { SafeStartGuide(); };

            Closed += delegate
            {
                _closed = true;
                _focusTimer.Enabled = false;
                _capture.LineReceived -= CaptureOnLineReceived;
                _nur.TagsInventoryReady -= NurOnTagsReady;
                _nur.Dispose();
            };
        }

        public bool Completed
        {
            get { return _finished; }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        private string _lastError = "";

        public ArrayList Entries
        {
            get { return _entries; }
        }

        public string SessionJson
        {
            get
            {
                return ScanSessionJson.BuildSessionObject(_entries, _finished, Steps.Length);
            }
        }

        private void SafeStartGuide()
        {
            try
            {
                _nur.Start();
                UpdateHardwareHint();
            }
            catch (Exception ex)
            {
                _lastError = "NUR: " + ex.Message;
                HttpResult res = AuditErrorReporter.ReportSync(
                    _cfg, "scan_guide_nur_start", ex, _nur.Status);
                _detail.Text = res.Ok
                    ? "NUR error logged on server"
                    : ("Log failed: " + ShortErr(res.Error));
            }

            _focusTimer.Enabled = true;
            _capture.ArmCapture();
            try
            {
                ShowStep();
            }
            catch (Exception ex)
            {
                ReportStepError("scan_guide_show_step", ex);
            }
        }

        private static string ShortErr(string s)
        {
            if (s == null || s.Length == 0) return "";
            return s.Length > 60 ? s.Substring(0, 59) + "~" : s;
        }

        private void ReportStepError(string context, Exception ex)
        {
            _lastError = ex.Message;
            AuditErrorReporter.ReportSync(_cfg, context, ex, _nur.Status);
            _lastRead.Text = "Error logged to server";
        }

        private void UpdateHardwareHint()
        {
            string nur = _nur.Status;
            string wedge = "Wedge: focus capture ON";
            _detail.Text = nur + "\r\n" + wedge;
        }

        private void ShowStep()
        {
            if (_stepIndex >= Steps.Length)
            {
                Finish(true);
                return;
            }
            string expected = Steps[_stepIndex][0];
            _instruction.Text = Steps[_stepIndex][1];
            _lastRead.Text = "Waiting for scan…";
            _detail.Text = "Step " + (_stepIndex + 1) + " / " + Steps.Length
                + "  " + expected.ToUpper()
                + "\r\n" + _nur.Status;
            _capture.ArmCapture();

            if (expected == "rfid" && _nur.IsAvailable)
            {
                _lastRead.Text = "Pull trigger (NUR API)…";
                try
                {
                    _nur.TriggerInventory();
                }
                catch (Exception ex)
                {
                    ReportStepError("scan_guide_trigger", ex);
                }
            }
            else if (expected == "rfid")
            {
                _lastRead.Text = "Pull trigger (wedge)…";
            }
            else if (expected == "barcode")
            {
                _lastRead.Text = "Press SCAN key…";
            }
        }

        private void GuidedScanForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (_closed) return;
            _capture.FeedKeyPress(e);
        }

        private void GuidedScanForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_closed) return;
            if (e.KeyCode == Keys.F1 || e.KeyCode == Keys.F9)
            {
                if (_nur.IsAvailable)
                {
                    _nur.TriggerInventory();
                    _lastRead.Text = "Trigger inventory…";
                }
                e.Handled = true;
            }
        }

        private void CaptureOnLineReceived(object sender, WedgeLineEventArgs e)
        {
            if (_closed || _stepIndex >= Steps.Length) return;
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { CaptureOnLineReceived(sender, e); }), null, EventArgs.Empty);
                return;
            }
            AdvanceStep(e.Line, "wedge_focus");
        }

        private void NurOnTagsReady(object sender, NurTagsEventArgs e)
        {
            try
            {
                NurOnTagsReadyCore(sender, e);
            }
            catch (Exception ex)
            {
                ReportStepError("scan_guide_nur_tags", ex);
            }
        }

        private void NurOnTagsReadyCore(object sender, NurTagsEventArgs e)
        {
            if (_closed || _stepIndex >= Steps.Length) return;
            if (e.WedgeText == null || e.WedgeText.Length == 0) return;

            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { NurOnTagsReady(sender, e); }), null, EventArgs.Empty);
                return;
            }

            string expected = Steps[_stepIndex][0];
            if (expected != "rfid" && expected != "any") return;

            long now = DateTime.UtcNow.Ticks;
            if (!e.IsComplete)
            {
                long minTicks = 600 * 10000L;
                if (now - _lastNurEmitTicks < minTicks) return;
            }
            _lastNurEmitTicks = now;

            _lastRead.Text = "NUR tags: " + e.WedgeText.Length + " chars";
            if (e.IsComplete || e.WedgeText.IndexOf(',') >= 0)
            {
                AdvanceStep(e.WedgeText, "nur_api");
            }
        }

        private void AdvanceStep(string rawLine, string source)
        {
            if (_closed) return;

            if (rawLine != null)
            {
                string expected = Steps[_stepIndex][0];
                string classified = ScanInputClassifier.Classify(rawLine);
                bool match = expected == "any"
                    || string.Compare(expected, classified, StringComparison.OrdinalIgnoreCase) == 0;

                var entry = new ScanCaptureEntry();
                entry.StepIndex = _stepIndex + 1;
                entry.StepLabel = Steps[_stepIndex][1];
                entry.ExpectedType = expected;
                entry.RawText = rawLine;
                entry.TrimmedText = rawLine.Trim();
                entry.ClassifiedType = classified;
                entry.Delimiters = ScanInputClassifier.DelimiterStats(rawLine);
                entry.TimestampUtc = DateTime.UtcNow.Ticks / 10000L;
                entry.Source = source ?? "wedge_focus";
                entry.MatchesExpected = match;
                _entries.Add(entry);

                string preview = entry.TrimmedText;
                if (preview.Length > 48) preview = preview.Substring(0, 47) + "~";
                _lastRead.Text = (match ? "OK " : "?? ")
                    + classified + " [" + entry.Source + "]: " + preview;
            }
            else
            {
                _lastRead.Text = "(step skipped)";
            }

            _stepIndex++;
            if (_stepIndex >= Steps.Length)
            {
                Finish(true);
                return;
            }
            ShowStep();
        }

        private void Finish(bool completed)
        {
            if (_closed) return;
            _closed = true;
            _finished = completed;
            _focusTimer.Enabled = false;
            _capture.LineReceived -= CaptureOnLineReceived;
            _nur.TagsInventoryReady -= NurOnTagsReady;
            DialogResult = completed ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }
    }
}
