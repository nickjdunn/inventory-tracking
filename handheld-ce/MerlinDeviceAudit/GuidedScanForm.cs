using System;
using System.Collections;
using System.Drawing;
using System.Windows.Forms;

namespace MerlinAudit
{
    /// <summary>
    /// Walks the operator through trigger RFID and Scan-key barcode reads.
    /// Captures raw wedge data for server-side analysis.
    /// </summary>
    internal sealed class GuidedScanForm : Form
    {
        private readonly WedgeInputCapture _capture = new WedgeInputCapture();
        private readonly ArrayList _entries = new ArrayList();
        private readonly Label _instruction;
        private readonly Label _detail;
        private readonly Label _lastRead;
        private readonly Button _skipBtn;
        private readonly Button _cancelBtn;
        private int _stepIndex;
        private bool _finished;
        private bool _closed;

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

        public GuidedScanForm()
        {
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
                Text = "Wedge capture ON each step.\r\nDo not tap other fields.",
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
            _skipBtn.Click += delegate { AdvanceStep(null); };
            btnRow.Controls.Add(_skipBtn);
            btnRow.Controls.Add(_cancelBtn);

            Controls.Add(_lastRead);
            Controls.Add(btnRow);
            Controls.Add(_detail);
            Controls.Add(_instruction);
            Controls.Add(_capture);
            _capture.BringToFront();

            _capture.LineReceived += CaptureOnLineReceived;

            Load += delegate
            {
                _capture.ArmCapture();
                ShowStep();
            };

            Closed += delegate
            {
                _capture.LineReceived -= CaptureOnLineReceived;
            };
        }

        public bool Completed
        {
            get { return _finished; }
        }

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

        private void ShowStep()
        {
            if (_stepIndex >= Steps.Length)
            {
                Finish(true);
                return;
            }
            _instruction.Text = Steps[_stepIndex][1];
            _lastRead.Text = "Waiting for scan…";
            _detail.Text = "Step " + (_stepIndex + 1) + " / " + Steps.Length
                + "\r\nExpected: " + Steps[_stepIndex][0].ToUpper();
            _capture.ArmCapture();
        }

        private void CaptureOnLineReceived(object sender, WedgeLineEventArgs e)
        {
            if (_closed || _stepIndex >= Steps.Length) return;
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { CaptureOnLineReceived(sender, e); }), null, EventArgs.Empty);
                return;
            }
            AdvanceStep(e.Line);
        }

        private void AdvanceStep(string rawLine)
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
                entry.MatchesExpected = match;
                _entries.Add(entry);

                string preview = entry.TrimmedText;
                if (preview.Length > 48) preview = preview.Substring(0, 47) + "~";
                _lastRead.Text = (match ? "OK " : "?? ")
                    + classified + ": " + preview;
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
            _capture.LineReceived -= CaptureOnLineReceived;
            DialogResult = completed ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }
    }
}
