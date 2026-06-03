using System;
using System.Drawing;
using System.Windows.Forms;

namespace MerlinAudit
{
    internal sealed class WedgeProbeForm : Form
    {
        private readonly AuditConfig _cfg;
        private readonly WedgeInputCapture _capture = new WedgeInputCapture();
        private readonly TextBox _log;
        private readonly Label _status;
        private int _count;

        public WedgeProbeForm(AuditConfig cfg)
        {
            _cfg = cfg ?? new AuditConfig();
            UiTheme.ApplyForm(this);
            Text = "Wedge probe";
            Width = 240;
            Height = 320;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            _status = UiTheme.MakeHeader("Scan / type — no tap needed");
            CfLayout.Place(_status, 6, 4, 228, 18);

            _log = UiTheme.MakeLogBox();
            CfLayout.Place(_log, 6, 24, 228, 220);

            var hint = UiTheme.MakeHint("SCAN key or keyboard wedge. 1=arm 0=back");
            CfLayout.Place(hint, 6, 248, 228, 14);

            var btnArm = UiTheme.MakeHotkeyButton(1, "Re-arm", true);
            CfLayout.Place(btnArm, 6, 264, 228, 32);
            btnArm.Click += delegate { _capture.ArmCapture(); _status.Text = "Armed"; };

            CfLayout.Place(_capture, 0, 0, 1, 1);
            _capture.LineReceived += CaptureOnLine;
            _capture.Activity += delegate
            {
                _status.Text = "Buf " + _capture.BufferLength + " chars";
            };

            Controls.Add(btnArm);
            Controls.Add(hint);
            Controls.Add(_log);
            Controls.Add(_status);
            Controls.Add(_capture);
            _capture.BringToFront();

            KeyPress += delegate(object s, KeyPressEventArgs e) { _capture.FeedKeyPress(e); };
            KeyDown += WedgeProbeForm_KeyDown;

            Load += delegate
            {
                _capture.ArmCapture();
                TestSessionLog.Add("wedge_probe", "opened", "");
            };
        }

        private void WedgeProbeForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { Close(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1)
            {
                _capture.ArmCapture();
                e.Handled = true;
            }
        }

        private void CaptureOnLine(object sender, WedgeLineEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { CaptureOnLine(sender, e); }), null, EventArgs.Empty);
                return;
            }
            _count++;
            string line = e.Line ?? "";
            string kind = ScanInputClassifier.Classify(line);
            string row = _count + " " + kind + " len=" + line.Length + "\r\n" + line + "\r\n\r\n";
            _log.Text = row + _log.Text;
            _status.Text = "OK " + kind + " (" + line.Length + " chars)";
            TestSessionLog.Add("wedge_probe", kind + " len=" + line.Length, line);
        }
    }
}
