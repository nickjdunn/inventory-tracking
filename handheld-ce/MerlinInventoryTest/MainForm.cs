using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MerlinHandheld
{
    public class MainForm : Form
    {
        private readonly AppConfig _cfg = AppConfig.Load();
        private readonly HandheldState _state = new HandheldState();
        private readonly InventoryApiClient _api;
        private readonly Panel _host;
        private readonly Label _topStatus;
        private readonly Timer _heartbeatTimer;

        private ReceivePanel _receive;
        private FindPanel _find;
        private AddPanel _add;
        private SettingsPanel _settings;
        private string _mode = "Receive";

        private readonly Button _btnReceive;
        private readonly Button _btnFind;
        private readonly Button _btnAdd;
        private readonly Button _btnSet;

        public MainForm()
        {
            _api = new InventoryApiClient(_cfg);
            Text = "Merlin Inventory " + AppConfig.AppVersion;
            Width = 320;
            Height = 480;
            MinimizeBox = false;
            KeyPreview = true;

            _topStatus = new Label
            {
                Text = "Starting…",
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Font = new Font("Tahoma", 8f)
            };

            _host = new Panel { Dock = DockStyle.Fill };

            var nav = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            _btnReceive = MakeNavButton("Receive", 0);
            _btnFind = MakeNavButton("Find", 1);
            _btnAdd = MakeNavButton("Add", 2);
            _btnSet = MakeNavButton("Set", 3);
            nav.Controls.Add(_btnReceive);
            nav.Controls.Add(_btnFind);
            nav.Controls.Add(_btnAdd);
            nav.Controls.Add(_btnSet);

            Controls.Add(_host);
            Controls.Add(nav);
            Controls.Add(_topStatus);

            _receive = new ReceivePanel(_cfg, _state, _api);
            _find = new FindPanel(_cfg, _state, _api);
            _add = new AddPanel(_cfg, _state, _api);
            _settings = new SettingsPanel(_cfg, _state, _api);
            _receive.StatusChanged += delegate { UpdateTopStatus(); };
            _settings.SyncCompleted += delegate
            {
                _receive.RefreshBins();
                _add.RefreshBins();
                _find.RefreshList();
                UpdateTopStatus();
            };

            _btnReceive.Click += delegate { ShowMode("Receive"); };
            _btnFind.Click += delegate { ShowMode("Find"); };
            _btnAdd.Click += delegate { ShowMode("Add"); };
            _btnSet.Click += delegate { ShowMode("Set"); };

            KeyDown += MainForm_KeyDown;

            _heartbeatTimer = new Timer();
            _heartbeatTimer.Interval = 30000;
            _heartbeatTimer.Tick += delegate { ThreadPool.QueueUserWorkItem(delegate { _api.ScannerPing(); }); };
            _heartbeatTimer.Enabled = true;

            Load += MainForm_Load;
            ShowMode(_cfg.LastMode != null && _cfg.LastMode.Length > 0 ? _cfg.LastMode : "Receive");
        }

        private Button MakeNavButton(string text, int index)
        {
            return new Button
            {
                Text = text,
                Width = 76,
                Height = 36,
                Left = 4 + index * 78,
                Top = 4,
                Font = new Font("Tahoma", 8f, FontStyle.Bold)
            };
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            UpdateTopStatus();
            ThreadPool.QueueUserWorkItem(delegate
            {
                _api.ScannerPing();
                HttpResult res = _api.FullSync();
                string err = "";
                bool ok = res.Ok && _api.TryApplyFullSync(res.Body, _state, out err);
                BeginInvoke(new EventHandler(delegate
                {
                    if (ok)
                    {
                        _receive.RefreshBins();
                        _add.RefreshBins();
                        _find.RefreshList();
                    }
                    _topStatus.Text = ok ? _state.LastMessage : ("Sync failed: " + err);
                    CheckForAppUpdate(false);
                }), null, EventArgs.Empty);
            });
        }

        private void CheckForAppUpdate(bool alwaysPromptOnError)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult upd = UpdateChecker.Check(_api, AppConfig.AppVersion);
                if (!upd.UpdateAvailable && !alwaysPromptOnError) return;
                BeginInvoke(new EventHandler(delegate
                {
                    if (upd.UpdateAvailable)
                    {
                        UpdateChecker.PromptIfUpdateAvailable(upd, _cfg.ServerUrl);
                        _topStatus.Text = "Update " + upd.ServerVersion + " available (you have " + AppConfig.AppVersion + ")";
                    }
                    else if (alwaysPromptOnError)
                    {
                        MessageBox.Show(
                            "You are on the latest version (" + AppConfig.AppVersion + ").",
                            "Merlin Inventory",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }), null, EventArgs.Empty);
            });
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1)
            {
                if (_mode == "Receive")
                {
                    string t = PromptWedgeRead();
                    if (t != null) _receive.SetWedgeText(t);
                }
                else if (_mode == "Find")
                {
                    string t = PromptWedgeRead();
                    if (t != null) _find.OnTriggerRead(t);
                }
                else if (_mode == "Add")
                {
                    string t = PromptWedgeRead();
                    if (t != null) _add.SetEpc(t);
                }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2)
            {
                if (_mode == "Add")
                {
                    string u = PromptScanRead();
                    if (u != null) _add.SetUpc(u);
                }
                e.Handled = true;
            }
        }

        private string PromptWedgeRead()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "RFID read (Trigger)";
                dlg.Width = 280;
                dlg.Height = 160;
                var tb = new TextBox
                {
                    Multiline = true,
                    Dock = DockStyle.Fill,
                    Font = new Font("Tahoma", 8f),
                    Text = "EPC001,EPC002"
                };
                var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 32 };
                ok.Click += delegate { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                dlg.Controls.Add(tb);
                dlg.Controls.Add(ok);
                return dlg.ShowDialog() == DialogResult.OK ? tb.Text : null;
            }
        }

        private string PromptScanRead()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "UPC (Scan key)";
                dlg.Width = 280;
                dlg.Height = 120;
                var tb = new TextBox { Dock = DockStyle.Top, Height = 28 };
                var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 32 };
                ok.Click += delegate { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                dlg.Controls.Add(ok);
                dlg.Controls.Add(tb);
                return dlg.ShowDialog() == DialogResult.OK ? tb.Text : null;
            }
        }

        private void ShowMode(string mode)
        {
            _mode = mode;
            _cfg.LastMode = mode;
            _cfg.Save();

            _host.Controls.Clear();
            UserControl panel;
            if (mode == "Find") panel = _find;
            else if (mode == "Add") panel = _add;
            else if (mode == "Set") panel = _settings;
            else panel = _receive;

            panel.Dock = DockStyle.Fill;
            _host.Controls.Add(panel);

            _btnReceive.BackColor = mode == "Receive" ? Color.FromArgb(56, 189, 248) : Color.FromArgb(51, 65, 85);
            _btnFind.BackColor = mode == "Find" ? Color.FromArgb(56, 189, 248) : Color.FromArgb(51, 65, 85);
            _btnAdd.BackColor = mode == "Add" ? Color.FromArgb(56, 189, 248) : Color.FromArgb(51, 65, 85);
            _btnSet.BackColor = mode == "Set" ? Color.FromArgb(56, 189, 248) : Color.FromArgb(51, 65, 85);

            if (mode == "Find") _find.RefreshList();
            UpdateTopStatus();
        }

        private void UpdateTopStatus()
        {
            string bin = _cfg.LastBinId != null && _cfg.LastBinId.Length > 0 ? _cfg.LastBinId : "—";
            _topStatus.Text = _state.LastMessage + " | Bin:" + bin;
        }
    }
}
