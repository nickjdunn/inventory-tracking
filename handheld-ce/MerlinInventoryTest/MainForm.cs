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
        private readonly HardwareBridge _hardware;
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

            _hardware = new HardwareBridge(this, _cfg);
            _hardware.RfidDataReceived += HardwareOnRfid;
            _hardware.BarcodeReceived += HardwareOnBarcode;

            _receive.StatusChanged += delegate { UpdateTopStatus(); };
            _settings.SyncCompleted += delegate
            {
                _receive.RefreshBins();
                _add.RefreshBins();
                _find.RefreshList();
                UpdateTopStatus();
            };
            _settings.HuntSyncCompleted += delegate
            {
                _find.RefreshList();
                _find.RefreshHuntDisplay();
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
            FormClosed += delegate { if (_hardware != null) _hardware.Dispose(); };
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
            ApplyHardwareModeForView();
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
                    UpdateTopStatus();
                    CheckForAppUpdate(false);
                }), null, EventArgs.Empty);
            });
        }

        private void ApplyHardwareModeForView()
        {
            if (_mode == "Add")
            {
                _hardware.SetInputMode("barcode");
            }
            else
            {
                _hardware.SetInputMode("rfid");
            }
        }

        private void HardwareOnRfid(object sender, HardwareRfidEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { HardwareOnRfid(sender, e); }), null, EventArgs.Empty);
                return;
            }
            if (_mode == "Receive")
            {
                _receive.SetWedgeText(e.WedgeText);
            }
            else if (_mode == "Find")
            {
                _find.OnTriggerRead(e.WedgeText);
            }
            else if (_mode == "Add")
            {
                _add.SetEpc(FirstEpcFromWedge(e.WedgeText));
            }
        }

        private void HardwareOnBarcode(object sender, HardwareBarcodeEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler(delegate { HardwareOnBarcode(sender, e); }), null, EventArgs.Empty);
                return;
            }
            if (_mode == "Add")
            {
                _add.SetUpc(e.Code);
            }
        }

        private static string FirstEpcFromWedge(string wedgeText)
        {
            if (wedgeText == null) return "";
            string[] parts = wedgeText.Split(new char[] { ',', '\n', '\r', '\t', ' ' });
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && parts[i].Trim().Length > 0)
                {
                    return parts[i].Trim();
                }
            }
            return wedgeText.Trim();
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
                _hardware.SetInputMode("rfid");
                if (_hardware.NurAvailable)
                {
                    _hardware.FireTriggerInventory();
                }
                else
                {
                    _hardware.ArmWedgeCapture();
                    _topStatus.Text = "Pull trigger or scan into wedge…";
                }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2)
            {
                if (_mode == "Add")
                {
                    _hardware.SetInputMode("barcode");
                    _hardware.ArmWedgeCapture();
                    _topStatus.Text = "Scan barcode (Scan key)…";
                }
                e.Handled = true;
            }
        }

        private void ShowMode(string mode)
        {
            _mode = mode;
            _cfg.LastMode = mode;
            _cfg.Save();
            ApplyHardwareModeForView();

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

            if (mode == "Find")
            {
                _find.RefreshList();
                _find.RefreshHuntDisplay();
            }
            UpdateTopStatus();
        }

        private void UpdateTopStatus()
        {
            string bin = _cfg.LastBinId != null && _cfg.LastBinId.Length > 0 ? _cfg.LastBinId : "—";
            string hw = _hardware != null ? _hardware.StatusLine : "";
            _topStatus.Text = _state.LastMessage + " | Bin:" + bin + " | " + hw;
        }
    }
}
