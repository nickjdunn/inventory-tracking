using System;
using System.Collections;
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
        private readonly System.Windows.Forms.Timer _heartbeatTimer;

        private ReceivePanel _receive;
        private FindPanel _find;
        private AddPanel _add;
        private SettingsPanel _settings;
        private DiagnosticsPanel _diagnostics;
        private readonly ScannerStreamService _stream;
        private string _mode = "Receive";

        private readonly Button _btnReceive;
        private readonly Button _btnFind;
        private readonly Button _btnAdd;
        private readonly Button _btnSet;
        private readonly Button _btnDiag;
        private readonly Label _versionFooter;

        public MainForm()
        {
            _api = new InventoryApiClient(_cfg);
            DiagnosticLog.SetUploadClient(_api);
            DiagnosticLog.Configure(_cfg.DiagnosticLogFile);
            if (_cfg.DiagnosticLogFile)
            {
                DiagnosticLog.LogSessionStart(_cfg);
            }

            _stream = new ScannerStreamService(_cfg, _api);
            MerlinUi.StyleForm(this);
            Text = "Merlin " + AppConfig.AppVersion;
            KeyPreview = true;

            _topStatus = new Label
            {
                Text = "Starting…",
                Dock = DockStyle.Top,
                Height = MerlinUi.StatusH,
                TextAlign = ContentAlignment.TopLeft,
                BackColor = MerlinUi.Card,
                ForeColor = Color.White,
                Font = MerlinUi.FontSm,
            };

            _host = new Panel { Dock = DockStyle.Fill, BackColor = MerlinUi.Bg };

            var nav = new Panel { Dock = DockStyle.Bottom, Height = MerlinUi.NavH, BackColor = MerlinUi.Card };
            int navW = MerlinUi.ScreenW / 5;
            _btnReceive = MakeNavButton("Recv", 0, navW);
            _btnFind = MakeNavButton("Find", 1, navW);
            _btnAdd = MakeNavButton("Add", 2, navW);
            _btnSet = MakeNavButton("Set", 3, navW);
            _btnDiag = MakeNavButton("Diag", 4, navW);
            nav.Controls.Add(_btnReceive);
            nav.Controls.Add(_btnFind);
            nav.Controls.Add(_btnAdd);
            nav.Controls.Add(_btnSet);
            nav.Controls.Add(_btnDiag);

            _versionFooter = new Label
            {
                Text = AppConfig.AppVersion,
                Dock = DockStyle.Bottom,
                Height = MerlinUi.FooterH,
                TextAlign = ContentAlignment.TopCenter,
                BackColor = MerlinUi.Card,
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = MerlinUi.FontSm,
            };

            Controls.Add(_host);
            Controls.Add(nav);
            Controls.Add(_versionFooter);
            Controls.Add(_topStatus);

            _receive = new ReceivePanel(_cfg, _state, _api);
            _find = new FindPanel(_cfg, _state, _api);
            _add = new AddPanel(_cfg, _state, _api);
            _settings = new SettingsPanel(_cfg, _state, _api);
            _diagnostics = new DiagnosticsPanel(_cfg, _api);

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
            _settings.AppExitRequested += delegate { RequestExit(); };
            _settings.VersionCheckCompleted += delegate
            {
                UpdateTopStatus();
            };

            _btnReceive.Click += delegate { ShowMode("Receive"); };
            _btnFind.Click += delegate { ShowMode("Find"); };
            _btnAdd.Click += delegate { ShowMode("Add"); };
            _btnSet.Click += delegate { ShowMode("Set"); };
            _btnDiag.Click += delegate { ShowMode("Diag"); };

            KeyDown += MainForm_KeyDown;

            _heartbeatTimer = new System.Windows.Forms.Timer();
            _heartbeatTimer.Interval = _cfg.LiveRawStream ? 10000 : 30000;
            _heartbeatTimer.Tick += HeartbeatTimer_Tick;
            _heartbeatTimer.Enabled = true;

            Load += MainForm_Load;
            Closed += MainForm_Closed;
            ShowMode(_cfg.LastMode != null && _cfg.LastMode.Length > 0 ? _cfg.LastMode : "Receive");
        }

        private void MainForm_Closed(object sender, EventArgs e)
        {
            _heartbeatTimer.Enabled = false;
            _hardware.Dispose();
        }

        public void RequestExit()
        {
            _cfg.Save();
            _heartbeatTimer.Enabled = false;
            _hardware.Dispose();
            Close();
            Application.Exit();
        }

        private Button MakeNavButton(string text, int index, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = MerlinUi.NavH - 4,
                Left = index * width,
                Top = 2,
                Font = MerlinUi.FontSmBold,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(51, 65, 85),
            };
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            UpdateTopStatus();
            ApplyHardwareModeForView();
            ThreadPool.QueueUserWorkItem(delegate
            {
                _api.RegisterScannerSession();
                if (_cfg.LiveRawStream)
                {
                    _api.PostScannerLive(
                        "session",
                        "App started — live stream enabled",
                        null,
                        null,
                        false,
                        "startup");
                }
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
                    RefreshVersionStatus(false);
                }), null, EventArgs.Empty);
            });
        }

        private void RefreshVersionStatus(bool alwaysPrompt)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult upd = UpdateChecker.Check(_api, AppConfig.AppVersion);
                BeginInvoke(new EventHandler(delegate
                {
                    if (upd.Reachable && !upd.UpdateAvailable)
                    {
                        _state.LastMessage = "Git up to date";
                    }
                    else if (upd.UpdateAvailable)
                    {
                        _state.LastMessage = MerlinUi.ShortLine("Update: " + upd.ServerVersion, 42);
                        if (alwaysPrompt)
                        {
                            UpdateChecker.PromptIfUpdateAvailable(upd, _cfg.ServerUrl);
                        }
                    }
                    UpdateTopStatus();
                }), null, EventArgs.Empty);
            });
        }

        private void HeartbeatTimer_Tick(object sender, EventArgs e)
        {
            _heartbeatTimer.Interval = _cfg.LiveRawStream ? 10000 : 30000;
            ThreadPool.QueueUserWorkItem(delegate
            {
                _api.PostScannerHeartbeat("native", false);
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
            _stream.OnRfid(e.WedgeText, _mode, e.Source);
            if (DiagnosticLog.IsEnabled)
            {
                _diagnostics.RefreshTracePreview();
            }
            if (_mode == "Diag")
            {
                _diagnostics.ShowLastRead(e.WedgeText);
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
            _stream.OnBarcode(e.Code, _mode);
            if (_mode == "Diag")
            {
                _diagnostics.ShowLastRead("UPC:" + e.Code);
            }
            if (_mode == "Add")
            {
                _add.SetUpc(e.Code);
            }
        }

        private static string FirstEpcFromWedge(string wedgeText)
        {
            ArrayList tags = ScanLimits.ParseTags(wedgeText);
            if (tags.Count == 0) return "";
            return ((TagRead)tags[0]).Epc;
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
                            "Merlin Inventory");
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
            else if (mode == "Diag") panel = _diagnostics;
            else panel = _receive;

            panel.Dock = DockStyle.Fill;
            _host.Controls.Add(panel);

            if (mode == "Diag")
            {
                _diagnostics.OnPanelShown();
            }
            else
            {
                _diagnostics.OnPanelHidden();
            }

            _btnReceive.BackColor = mode == "Receive" ? MerlinUi.Accent : Color.FromArgb(51, 65, 85);
            _btnFind.BackColor = mode == "Find" ? MerlinUi.Accent : Color.FromArgb(51, 65, 85);
            _btnAdd.BackColor = mode == "Add" ? MerlinUi.Accent : Color.FromArgb(51, 65, 85);
            _btnSet.BackColor = mode == "Set" ? MerlinUi.Accent : Color.FromArgb(51, 65, 85);
            _btnDiag.BackColor = mode == "Diag" ? MerlinUi.Accent : Color.FromArgb(51, 65, 85);
            _btnReceive.ForeColor = mode == "Receive" ? MerlinUi.Bg : Color.White;
            _btnFind.ForeColor = mode == "Find" ? MerlinUi.Bg : Color.White;
            _btnAdd.ForeColor = mode == "Add" ? MerlinUi.Bg : Color.White;
            _btnSet.ForeColor = mode == "Set" ? MerlinUi.Bg : Color.White;
            _btnDiag.ForeColor = mode == "Diag" ? MerlinUi.Bg : Color.White;

            if (mode == "Find")
            {
                _find.RefreshList();
                _find.RefreshHuntDisplay();
            }
            UpdateTopStatus();
        }

        private void UpdateTopStatus()
        {
            string bin = _cfg.LastBinId != null && _cfg.LastBinId.Length > 0 ? _cfg.LastBinId : "-";
            string line = _state.LastMessage + " | " + bin;
            _topStatus.Text = MerlinUi.ShortLine(line, 42);
        }
    }
}
