using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MerlinAudit
{
    internal sealed class TriggerProbeForm : Form
    {
        private readonly AuditConfig _cfg;
        private readonly NurApiBridge _nur;
        private readonly TextBox _log;
        private readonly Label _status;

        public TriggerProbeForm(AuditConfig cfg)
        {
            _cfg = cfg ?? new AuditConfig();
            UiTheme.ApplyForm(this);
            Text = "Trigger / NUR";
            Width = 240;
            Height = 320;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            _nur = new NurApiBridge(this, _cfg);
            _nur.TagReadingsReady += NurOnReadings;
            _nur.TagsInventoryReady += NurOnTagsLegacy;

            _status = UiTheme.MakeHeader("NUR trigger log");
            CfLayout.Place(_status, 6, 4, 228, 18);

            _log = UiTheme.MakeLogBox();
            CfLayout.Place(_log, 6, 24, 228, 220);

            var hint = UiTheme.MakeHint("Pull trigger. 1=stream 2=stop 0=back");
            CfLayout.Place(hint, 6, 248, 228, 14);

            var btnStart = UiTheme.MakeHotkeyButton(1, "Start stream", true);
            CfLayout.Place(btnStart, 6, 264, 110, 32);
            btnStart.Click += delegate { _nur.EnsureInventoryStream(); Log("stream start"); };

            var btnStop = UiTheme.MakeHotkeyButton(2, "Stop", false);
            CfLayout.Place(btnStop, 124, 264, 110, 32);
            btnStop.Click += delegate { _nur.StopInventoryStreamSafe(); Log("stream stop"); };

            Controls.Add(btnStop);
            Controls.Add(btnStart);
            Controls.Add(hint);
            Controls.Add(_log);
            Controls.Add(_status);

            KeyDown += TriggerProbeForm_KeyDown;
            Load += delegate
            {
                _nur.Start();
                _status.Text = _nur.Status;
                TestSessionLog.Add("trigger_probe", "opened", _nur.Status);
            };
            Closed += delegate
            {
                _nur.TagReadingsReady -= NurOnReadings;
                _nur.TagsInventoryReady -= NurOnTagsLegacy;
                _nur.Dispose();
            };
        }

        private void TriggerProbeForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { Close(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1)
            {
                _nur.EnsureInventoryStream();
                Log("keypad start");
                e.Handled = true;
            }
            if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2)
            {
                _nur.StopInventoryStreamSafe();
                Log("keypad stop");
                e.Handled = true;
            }
        }

        private void NurOnReadings(object sender, NurTagReadingsEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { NurOnReadings(sender, e); }), null, EventArgs.Empty);
                return;
            }
            var sb = new StringBuilder();
            sb.Append(e.Readings.Length).Append(" tag(s)\r\n");
            for (int i = 0; i < e.Readings.Length && i < 8; i++)
            {
                NurTagReading r = e.Readings[i];
                sb.Append(r.RssiLabel).Append(" ").Append(r.Epc).Append("\r\n");
            }
            Log("nur_readings\r\n" + sb);
            TestSessionLog.Add("trigger_probe", e.Readings.Length + " tags", sb.ToString());
        }

        private void NurOnTagsLegacy(object sender, NurTagsEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { NurOnTagsLegacy(sender, e); }), null, EventArgs.Empty);
                return;
            }
            Log("nur_csv len=" + (e.WedgeText != null ? e.WedgeText.Length : 0));
            TestSessionLog.Add("trigger_probe", "csv", e.WedgeText ?? "");
        }

        private void Log(string text)
        {
            _log.Text = DateTime.UtcNow.ToString("HH:mm:ss") + " " + text + "\r\n\r\n" + _log.Text;
            _status.Text = _nur.Status;
        }
    }
}
