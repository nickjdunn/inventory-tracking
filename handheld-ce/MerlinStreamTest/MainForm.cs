using System;
using System.Collections;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MerlinStream
{
    /// <summary>Minimal stream test: every read goes straight to the server.</summary>
    public class MainForm : Form
    {
        private readonly StreamConfig _cfg = new StreamConfig();
        private readonly StreamClient _client;
        private readonly HardwareBridge _hardware;
        private readonly Label _status;
        private readonly Label _detail;
        private readonly TextBox _serverBox;
        private readonly TextBox _scannerBox;
        private readonly TextBox _binBox;
        private int _readCount;

        public MainForm()
        {
            _client = new StreamClient(_cfg);
            Text = "Merlin Stream " + StreamConfig.AppVersion;
            Width = 240;
            Height = 320;
            BackColor = Color.FromArgb(15, 23, 42);
            ForeColor = Color.White;
            KeyPreview = true;

            _status = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                ForeColor = Color.White,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                Text = "Stream test",
            };

            _detail = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Tahoma", 8f, FontStyle.Regular),
                Text = "Pick a mode below.\r\nF1 = RFID trigger.",
            };

            var nav = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(30, 41, 59) };
            var btnIgnore = MakeNav("Ign", "ignore", 0);
            var btnWatch = MakeNav("Watch", "watch", 80);
            var btnScan = MakeNav("Scan", "scan", 160);
            nav.Controls.Add(btnIgnore);
            nav.Controls.Add(btnWatch);
            nav.Controls.Add(btnScan);

            var setPanel = new Panel { Dock = DockStyle.Bottom, Height = 72 };
            _serverBox = MakeField(_cfg.ServerUrl);
            _scannerBox = MakeField(_cfg.ScannerId);
            _binBox = MakeField(_cfg.ScanBinId);
            var pingBtn = new Button
            {
                Text = "Ping",
                Width = 48,
                Height = 22,
                Left = 0,
                Top = 50,
            };
            pingBtn.Click += delegate { RunPing(); };
            setPanel.Controls.Add(pingBtn);
            setPanel.Controls.Add(_binBox);
            setPanel.Controls.Add(Lbl("Bin (scan mode)"));
            setPanel.Controls.Add(_scannerBox);
            setPanel.Controls.Add(Lbl("Scanner ID"));
            setPanel.Controls.Add(_serverBox);
            setPanel.Controls.Add(Lbl("Server"));

            Controls.Add(_detail);
            Controls.Add(setPanel);
            Controls.Add(nav);
            Controls.Add(_status);

            _hardware = new HardwareBridge(this, _cfg);
            _hardware.RfidDataReceived += HardwareOnRfid;

            KeyDown += MainForm_KeyDown;
            Load += delegate
            {
                ApplyMode(_cfg.ScreenMode);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    _client.PushSession("MerlinStreamTest started");
                });
            };

            Closed += delegate { _hardware.Dispose(); };
        }

        private static Label Lbl(string t)
        {
            return new Label
            {
                Text = t,
                Height = 14,
                Dock = DockStyle.Top,
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
            };
        }

        private static TextBox MakeField(string text)
        {
            return new TextBox
            {
                Text = text ?? "",
                Height = 20,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Tahoma", 8f, FontStyle.Regular),
            };
        }

        private Button MakeNav(string text, string mode, int left)
        {
            var b = new Button
            {
                Text = text,
                Left = left,
                Top = 2,
                Width = 76,
                Height = 24,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(51, 65, 85),
            };
            b.Click += delegate { ApplyMode(mode); };
            return b;
        }

        private void ApplyMode(string mode)
        {
            _cfg.ScreenMode = mode;
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.ScannerId = _scannerBox.Text.Trim();
            _cfg.ScanBinId = _binBox.Text.Trim();
            _readCount = 0;
            string hint;
            if (mode == "ignore")
            {
                hint = "IGNORE: streams to server only.\r\nNo inventory POST.";
            }
            else if (mode == "scan")
            {
                hint = "SCAN: stream + POST /api/scan\r\n(bin above, debounced 500ms)";
            }
            else
            {
                hint = "WATCH: stream all reads.\r\nShows count on gun only.";
            }
            _detail.Text = hint + "\r\n\r\nScans work without tapping a field.\r\nOpen PC:\r\n/deploy/scanner-stream-test.html";
            _status.Text = "Mode: " + mode.ToUpper();
            _client.PushSession("mode=" + mode);
        }

        private void HardwareOnRfid(object sender, HardwareRfidEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { HardwareOnRfid(sender, e); }), null, EventArgs.Empty);
                return;
            }

            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.ScannerId = _scannerBox.Text.Trim();
            _cfg.ScanBinId = _binBox.Text.Trim();

            ArrayList tags = ScanLimits.ParseTags(e.WedgeText);
            _readCount++;
            _status.Text = "Read #" + _readCount + " tags=" + tags.Count;
            _detail.Text = ScanLimits.FormatSummary(tags) + "\r\n\r\n→ streaming…";

            _client.PushRfid(e.Source, e.WedgeText, tags);
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1)
            {
                _hardware.SetInputMode("rfid");
                if (_hardware.NurAvailable)
                {
                    _hardware.FireTriggerInventory();
                }
                else
                {
                    _status.Text = "RFID scan ready";
                }
                e.Handled = true;
            }
        }

        private void RunPing()
        {
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _status.Text = "Ping…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult r = _client.Ping();
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = r.Ok ? "Ping OK" : ("Ping fail: " + r.Error);
                }), null, EventArgs.Empty);
            });
        }
    }
}
