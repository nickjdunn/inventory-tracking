using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MerlinAudit
{
    internal sealed class RssiMonitorForm : Form
    {
        private readonly AuditConfig _cfg;
        private readonly NurApiBridge _nur;
        private readonly Label _status;
        private readonly Label _peak;
        private readonly TextBox _log;
        private int _bestRssi = -999;
        private string _bestEpc = "";

        public RssiMonitorForm(AuditConfig cfg)
        {
            _cfg = cfg ?? new AuditConfig();
            UiTheme.ApplyForm(this);
            Text = "RSSI monitor";
            Width = 240;
            Height = 320;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _nur = new NurApiBridge(this, _cfg);
            _nur.TagReadingsReady += NurOnReadings;

            _status = UiTheme.MakeHeader("Starting NUR…");
            CfLayout.Place(_status, 6, 4, 228, 18);

            _peak = new Label
            {
                ForeColor = UiTheme.Good,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                Height = 16,
                Left = 6,
                Top = 22,
                Width = 228,
                Text = "Peak: —",
            };

            _log = UiTheme.MakeLogBox();
            CfLayout.Place(_log, 6, 42, 228, 196);

            var hint = UiTheme.MakeHint("Pull trigger. 1=scan 2=clear 0=back");
            CfLayout.Place(hint, 6, 242, 228, 14);

            var btnScan = UiTheme.MakeHotkeyButton(1, "Scan now", true);
            CfLayout.Place(btnScan, 6, 258, 110, 32);
            btnScan.Click += delegate { PollTags("manual"); };

            var btnClear = UiTheme.MakeHotkeyButton(2, "Clear", false);
            CfLayout.Place(btnClear, 124, 258, 110, 32);
            btnClear.Click += delegate { ClearLog(); };

            Controls.Add(btnClear);
            Controls.Add(btnScan);
            Controls.Add(hint);
            Controls.Add(_log);
            Controls.Add(_peak);
            Controls.Add(_status);

            KeyDown += RssiMonitorForm_KeyDown;
            Load += delegate
            {
                _nur.Start();
                _status.Text = _nur.Status;
                _nur.EnsureInventoryStream();
                TestSessionLog.Add("rssi_monitor", "opened", _nur.Status);
            };
            Closed += delegate
            {
                _nur.TagReadingsReady -= NurOnReadings;
                _nur.Dispose();
            };
        }

        private void RssiMonitorForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { Close(); e.Handled = true; return; }
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) { PollTags("keypad"); e.Handled = true; return; }
            if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) { ClearLog(); e.Handled = true; }
        }

        private void ClearLog()
        {
            _log.Text = "";
            _bestRssi = -999;
            _bestEpc = "";
            _peak.Text = "Peak: —";
        }

        private void NurOnReadings(object sender, NurTagReadingsEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { NurOnReadings(sender, e); }), null, EventArgs.Empty);
                return;
            }
            RenderReadings(e.Readings, "trigger");
        }

        private void PollTags(string source)
        {
            NurTagReading[] readings = _nur.ReadTagsNow();
            RenderReadings(readings, source);
        }

        private void RenderReadings(NurTagReading[] readings, string source)
        {
            if (readings == null || readings.Length == 0)
            {
                _status.Text = "No tags (" + source + ")";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < readings.Length && i < 12; i++)
            {
                NurTagReading r = readings[i];
                string epc = r.Epc;
                if (epc.Length > 22) epc = epc.Substring(0, 21) + "~";
                sb.Append(r.StrengthBar);
                sb.Append(" ");
                sb.Append(r.RssiLabel);
                sb.Append(" ");
                sb.Append(epc);
                sb.Append("\r\n");

                if (r.HasRssi && r.Rssi > _bestRssi)
                {
                    _bestRssi = r.Rssi;
                    _bestEpc = r.Epc;
                }
            }

            string line = DateTime.UtcNow.ToString("HH:mm:ss") + " [" + source + "] " + readings.Length + " tag(s)";
            _log.Text = line + "\r\n" + sb + "\r\n" + _log.Text;
            if (_log.Text.Length > 4000) _log.Text = _log.Text.Substring(0, 3999);

            if (_bestRssi > -999)
            {
                _peak.Text = "Peak: " + _bestRssi + " dBm  " + ShortEpc(_bestEpc);
            }

            _status.Text = _nur.Status + "  tags=" + readings.Length;
            TestSessionLog.Add("rssi_monitor", readings.Length + " tags", sb.ToString());
        }

        private static string ShortEpc(string epc)
        {
            if (epc == null || epc.Length == 0) return "";
            if (epc.Length <= 16) return epc;
            return epc.Substring(0, 15) + "~";
        }
    }
}
