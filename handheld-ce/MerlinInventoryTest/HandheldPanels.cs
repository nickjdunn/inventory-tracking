using System;
using System.Collections;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MerlinHandheld
{
    public class ReceivePanel : UserControl
    {
        private readonly AppConfig _cfg;
        private readonly HandheldState _state;
        private readonly InventoryApiClient _api;
        private readonly ComboBox _binCombo;
        private readonly TextBox _tagsBox;
        private readonly Label _resultLabel;
        private ArrayList _pendingTags = new ArrayList();
        private int _lastRawTagCount;
        public event EventHandler StatusChanged;

        public ReceivePanel(AppConfig cfg, HandheldState state, InventoryApiClient api)
        {
            _cfg = cfg;
            _state = state;
            _api = api;
            MerlinUi.StylePanel(this);

            _resultLabel = MerlinUi.MakeStatusLabel();
            _resultLabel.Text = "Ready";

            _tagsBox = MerlinUi.MakeField();
            _tagsBox.Multiline = true;
            _tagsBox.Height = 72;
            _tagsBox.ScrollBars = ScrollBars.Vertical;
            _tagsBox.Dock = DockStyle.Fill;

            var sendBtn = MerlinUi.MakePrimaryButton("Send tags");
            sendBtn.Click += delegate { SendTags(); };

            _binCombo = MerlinUi.MakeCombo();

            var hint = new Label
            {
                Text = "F1/trigger = RFID. Tags below.",
                Height = 24,
                Dock = DockStyle.Top,
                ForeColor = Color.FromArgb(148, 163, 184),
            };

            Controls.Add(_resultLabel);
            Controls.Add(_tagsBox);
            Controls.Add(sendBtn);
            Controls.Add(_binCombo);
            Controls.Add(MerlinUi.MakeCaption("Bin"));
            Controls.Add(hint);
        }

        public void RefreshBins()
        {
            string prev = _binCombo.SelectedValue as string;
            if (prev == null && _binCombo.SelectedItem != null)
            {
                var b = _binCombo.SelectedItem as BinInfo;
                if (b != null) prev = b.Id;
            }
            _binCombo.Items.Clear();
            for (int i = 0; i < _state.Bins.Count; i++)
            {
                _binCombo.Items.Add((BinInfo)_state.Bins[i]);
            }
            _binCombo.DisplayMember = "Display";
            _binCombo.ValueMember = "Id";
            if (prev != null && prev.Length > 0)
            {
                for (int i = 0; i < _binCombo.Items.Count; i++)
                {
                    var b = (BinInfo)_binCombo.Items[i];
                    if (string.Compare(b.Id, prev, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        _binCombo.SelectedIndex = i;
                        return;
                    }
                }
            }
            if (_binCombo.Items.Count > 0) _binCombo.SelectedIndex = 0;
        }

        public void SetWedgeText(string text)
        {
            ArrayList parsed = ScanLimits.ParseTags(text);
            _lastRawTagCount = parsed.Count;
            _pendingTags = parsed;
            _tagsBox.Text = ScanLimits.FormatSummary(_pendingTags);
            _resultLabel.Text = _pendingTags.Count + " tag(s) ready";
        }

        private void SendTags()
        {
            if (_binCombo.SelectedItem == null)
            {
                _resultLabel.Text = "Select a bin";
                return;
            }
            var bin = (BinInfo)_binCombo.SelectedItem;
            _cfg.LastBinId = bin.Id;
            _cfg.Save();

            ArrayList tags = _pendingTags;
            if (tags == null || tags.Count == 0)
            {
                tags = ScanLimits.ParseTags(_tagsBox.Text);
                _pendingTags = tags;
            }
            if (tags.Count == 0)
            {
                _resultLabel.Text = "No tags";
                return;
            }

            int sentCount = tags.Count;
            _resultLabel.Text = "Sending " + sentCount + "…";
            string binId = bin.Id;
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.PostScan(tags, binId);
                string summary = res.Ok
                    ? InventoryApiClient.FormatScanResult(res.Body)
                    : (res.Error.Length > 0 ? res.Error : res.Body);
                BeginInvoke(new EventHandler(delegate
                {
                    _resultLabel.Text = MerlinUi.ShortLine(summary, 120);
                    _state.LastScanSummary = summary;
                    if (StatusChanged != null) StatusChanged(this, EventArgs.Empty);
                }), null, EventArgs.Empty);
            });
        }
    }

    public class FindPanel : UserControl
    {
        private readonly AppConfig _cfg;
        private readonly HandheldState _state;
        private readonly InventoryApiClient _api;
        private readonly TextBox _searchBox;
        private readonly ListBox _list;
        private readonly Label _huntLabel;
        private string _swapOldEpc = "";

        public FindPanel(AppConfig cfg, HandheldState state, InventoryApiClient api)
        {
            _cfg = cfg;
            _state = state;
            _api = api;
            MerlinUi.StylePanel(this);

            _huntLabel = MerlinUi.MakeStatusLabel();
            _huntLabel.Height = 40;
            _huntLabel.Text = "Sync on Set tab first";

            var btnRow = new Panel { Height = MerlinUi.BtnH, Dock = DockStyle.Bottom };
            var clearBtn = MerlinUi.MakeButton("Clear");
            clearBtn.Dock = DockStyle.Right;
            clearBtn.Width = MerlinUi.ContentW / 2 - 2;
            clearBtn.Click += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    _api.PostHuntQueue(new ArrayList());
                    BeginInvoke(new EventHandler(delegate
                    {
                        _state.HuntQueue.Clear();
                        _huntLabel.Text = "Hunt cleared";
                    }), null, EventArgs.Empty);
                });
            };
            var huntBtn = MerlinUi.MakePrimaryButton("Hunt");
            huntBtn.Dock = DockStyle.Fill;
            huntBtn.Click += delegate { StartHunt(); };
            btnRow.Controls.Add(clearBtn);
            btnRow.Controls.Add(huntBtn);

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = MerlinUi.FontSm,
                BackColor = MerlinUi.Card,
                ForeColor = Color.White,
            };

            _searchBox = MerlinUi.MakeField();
            _searchBox.TextChanged += delegate { RefreshList(); };

            var refreshHuntBtn = MerlinUi.MakeButton("Refresh hunt");
            refreshHuntBtn.Click += delegate { RefreshHuntFromServer(); };

            var swapBtn = MerlinUi.MakeButton("Swap tag");
            swapBtn.Click += delegate { BeginSwapTag(); };

            Controls.Add(_huntLabel);
            Controls.Add(btnRow);
            Controls.Add(_list);
            Controls.Add(_searchBox);
            Controls.Add(MerlinUi.MakeCaption("Search"));
            Controls.Add(swapBtn);
            Controls.Add(refreshHuntBtn);
        }

        private void BeginSwapTag()
        {
            _swapOldEpc = "";
            if (_list.SelectedItem == null)
            {
                _huntLabel.Text = "Select item first";
                _huntLabel.ForeColor = Color.Salmon;
                return;
            }
            var it = (ItemInfo)_list.SelectedItem;
            _swapOldEpc = it.EpcId;
            _huntLabel.Text = MerlinUi.ShortLine("Scan NEW tag: " + it.Name, 80);
            _huntLabel.ForeColor = Color.Khaki;
        }

        private void CancelSwapTag()
        {
            _swapOldEpc = "";
        }

        public void RefreshHuntDisplay()
        {
            if (_state.HuntQueue.Count == 0)
            {
                _huntLabel.Text = "No hunt — pick item, Hunt";
                _huntLabel.ForeColor = Color.White;
                return;
            }
            string epc = (string)_state.HuntQueue[0];
            ItemInfo item = _state.FindItem(epc);
            string name = item != null ? item.Name : epc;
            _huntLabel.Text = MerlinUi.ShortLine("Hunt: " + name, 80);
            _huntLabel.ForeColor = Color.Khaki;
        }

        public void RefreshHuntFromServer()
        {
            _huntLabel.Text = "Refreshing…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.SyncSummary();
                string err = "";
                bool ok = res.Ok && _api.TryApplyHuntSummary(res.Body, _state, out err);
                BeginInvoke(new EventHandler(delegate
                {
                    if (ok) RefreshHuntDisplay();
                    else _huntLabel.Text = MerlinUi.ShortLine(err.Length > 0 ? err : res.Error, 80);
                }), null, EventArgs.Empty);
            });
        }

        public void RefreshList()
        {
            _list.Items.Clear();
            ArrayList filtered = _state.FilterItems(_searchBox.Text);
            for (int i = 0; i < filtered.Count; i++)
            {
                _list.Items.Add((ItemInfo)filtered[i]);
            }
            _list.DisplayMember = "ListLine";
        }

        public void OnTriggerRead(string wedgeText)
        {
            if (wedgeText == null || wedgeText.Length == 0) return;
            ArrayList tags = ScanLimits.ParseTags(wedgeText);
            if (tags.Count == 0) return;
            string epc = ((TagRead)tags[0]).Epc;

            if (_swapOldEpc != null && _swapOldEpc.Length > 0)
            {
                CompleteSwapTag(epc);
                return;
            }

            ItemInfo hit = _state.FindItem(epc);

            if (_state.HuntQueue.Count > 0)
            {
                _huntLabel.Text = "RSSI…";
                ThreadPool.QueueUserWorkItem(delegate
                {
                    _api.PostNearFieldIngest(tags);
                    HttpResult huntRes = _api.GetSearchTarget();
                    string signal = huntRes.Ok ? InventoryApiClient.FormatHuntSignal(huntRes.Body) : "";
                    BeginInvoke(new EventHandler(delegate
                    {
                        ShowTriggerResult(epc, hit, signal);
                    }), null, EventArgs.Empty);
                });
                return;
            }

            ShowTriggerResult(epc, hit, "");
        }

        private void ShowTriggerResult(string epc, ItemInfo hit, string huntSignal)
        {
            if (hit != null)
            {
                _huntLabel.Text = MerlinUi.ShortLine("OK " + hit.Name, 80);
                _huntLabel.ForeColor = Color.LightGreen;
                if (huntSignal.Length > 0)
                {
                    _huntLabel.Text = MerlinUi.ShortLine(_huntLabel.Text + " " + huntSignal, 90);
                }
                return;
            }
            _huntLabel.Text = MerlinUi.ShortLine(huntSignal.Length > 0 ? epc + " " + huntSignal : epc, 90);
            _huntLabel.ForeColor = Color.Salmon;
        }

        private void CompleteSwapTag(string newEpc)
        {
            string oldEpc = _swapOldEpc;
            _swapOldEpc = "";
            if (newEpc == null || newEpc.Length == 0)
            {
                _huntLabel.Text = "No EPC read";
                return;
            }
            if (string.Compare(oldEpc, newEpc, StringComparison.OrdinalIgnoreCase) == 0)
            {
                _huntLabel.Text = "New tag must differ";
                _huntLabel.ForeColor = Color.Salmon;
                return;
            }

            _huntLabel.Text = "Validating…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult valid = _api.ValidateEpc(newEpc);
                bool epcOk = valid.Ok && SimpleJson.ExtractBool(valid.Body, "valid", false);
                if (!epcOk)
                {
                    string err = SimpleJson.ExtractString(valid.Body, "error");
                    BeginInvoke(new EventHandler(delegate
                    {
                        _huntLabel.Text = MerlinUi.ShortLine(err.Length > 0 ? err : "EPC in use", 80);
                        _huntLabel.ForeColor = Color.Salmon;
                    }), null, EventArgs.Empty);
                    return;
                }

                HttpResult res = _api.ReplaceItemEpc(oldEpc, newEpc);
                BeginInvoke(new EventHandler(delegate
                {
                    if (!res.Ok)
                    {
                        string err = SimpleJson.ExtractString(res.Body, "error");
                        _huntLabel.Text = MerlinUi.ShortLine(err.Length > 0 ? err : res.Error, 80);
                        _huntLabel.ForeColor = Color.Salmon;
                        return;
                    }

                    ItemInfo item = _state.FindItem(oldEpc);
                    if (item != null)
                    {
                        item.EpcId = newEpc;
                    }
                    for (int i = 0; i < _state.HuntQueue.Count; i++)
                    {
                        if (string.Compare((string)_state.HuntQueue[i], oldEpc, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            _state.HuntQueue[i] = newEpc;
                        }
                    }
                    _huntLabel.Text = MerlinUi.ShortLine("Tag swapped OK", 80);
                    _huntLabel.ForeColor = Color.LightGreen;
                    RefreshList();
                }), null, EventArgs.Empty);
            });
        }

        private void StartHunt()
        {
            CancelSwapTag();
            if (_list.SelectedItem == null)
            {
                _huntLabel.Text = "Select item";
                return;
            }
            var it = (ItemInfo)_list.SelectedItem;
            var queue = new ArrayList();
            queue.Add(it.EpcId);
            _huntLabel.Text = "Starting…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.PostHuntQueue(queue);
                BeginInvoke(new EventHandler(delegate
                {
                    if (res.Ok)
                    {
                        _state.HuntQueue.Clear();
                        _state.HuntQueue.Add(it.EpcId);
                        _huntLabel.Text = MerlinUi.ShortLine("Hunt: " + it.Name, 80);
                        _huntLabel.ForeColor = Color.Khaki;
                    }
                    else
                    {
                        _huntLabel.Text = MerlinUi.ShortLine("Failed: " + res.Error, 80);
                    }
                }), null, EventArgs.Empty);
            });
        }
    }

    public class AddPanel : UserControl
    {
        private readonly AppConfig _cfg;
        private readonly HandheldState _state;
        private readonly InventoryApiClient _api;
        private readonly TextBox _upcBox;
        private readonly TextBox _nameBox;
        private readonly TextBox _epcBox;
        private readonly ComboBox _binCombo;
        private readonly Label _status;
        private string _lookupCategory = "";

        public AddPanel(AppConfig cfg, HandheldState state, InventoryApiClient api)
        {
            _cfg = cfg;
            _state = state;
            _api = api;
            MerlinUi.StylePanel(this);

            _status = MerlinUi.MakeStatusLabel();

            var saveBtn = MerlinUi.MakePrimaryButton("Register");
            saveBtn.Click += delegate { Register(); };

            _binCombo = MerlinUi.MakeCombo();
            _epcBox = MerlinUi.MakeField();
            _nameBox = MerlinUi.MakeField();
            _upcBox = MerlinUi.MakeField();

            var lookupBtn = MerlinUi.MakeButton("Lookup UPC");
            lookupBtn.Click += delegate { LookupUpc(); };

            var hint = new Label
            {
                Text = "F2 = barcode, F1 = EPC",
                Height = 20,
                Dock = DockStyle.Top,
                ForeColor = Color.FromArgb(148, 163, 184),
            };

            Controls.Add(_status);
            Controls.Add(saveBtn);
            Controls.Add(_binCombo);
            Controls.Add(MerlinUi.MakeCaption("Home bin"));
            Controls.Add(_epcBox);
            Controls.Add(MerlinUi.MakeCaption("EPC"));
            Controls.Add(_nameBox);
            Controls.Add(MerlinUi.MakeCaption("Name"));
            Controls.Add(lookupBtn);
            Controls.Add(_upcBox);
            Controls.Add(MerlinUi.MakeCaption("UPC"));
            Controls.Add(hint);
        }

        public void RefreshBins()
        {
            _binCombo.Items.Clear();
            for (int i = 0; i < _state.Bins.Count; i++) _binCombo.Items.Add((BinInfo)_state.Bins[i]);
            _binCombo.DisplayMember = "Display";
            if (_binCombo.Items.Count > 0) _binCombo.SelectedIndex = 0;
        }

        public void SetUpc(string upc)
        {
            if (upc != null) _upcBox.Text = upc;
        }

        public void SetEpc(string epc)
        {
            if (epc != null) _epcBox.Text = epc;
        }

        private void LookupUpc()
        {
            string upc = _upcBox.Text.Trim();
            if (upc.Length == 0) return;
            _status.Text = "Lookup…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.LookupUpc(upc);
                BeginInvoke(new EventHandler(delegate
                {
                    if (!res.Ok)
                    {
                        _status.Text = MerlinUi.ShortLine("Fail: " + res.Error, 80);
                        return;
                    }
                    bool found = SimpleJson.ExtractBool(res.Body, "found", false);
                    if (!found)
                    {
                        _status.Text = "UPC not found";
                        return;
                    }
                    string title = SimpleJson.ExtractString(res.Body, "name");
                    if (title.Length == 0) title = SimpleJson.ExtractString(res.Body, "title");
                    _nameBox.Text = title;
                    string cat = SimpleJson.ExtractString(res.Body, "category");
                    _lookupCategory = cat;
                    _status.Text = MerlinUi.ShortLine("Found: " + title, 80);
                }), null, EventArgs.Empty);
            });
        }

        private void Register()
        {
            string epc = _epcBox.Text.Trim();
            string name = _nameBox.Text.Trim();
            if (name.Length == 0)
            {
                _status.Text = "Name required";
                return;
            }
            if (epc.Length == 0)
            {
                _status.Text = "EPC required (F1)";
                return;
            }

            string homeBin = "";
            if (_binCombo.SelectedItem != null) homeBin = ((BinInfo)_binCombo.SelectedItem).Id;

            _status.Text = "Saving…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult valid = _api.ValidateEpc(epc);
                bool epcOk = valid.Ok && SimpleJson.ExtractBool(valid.Body, "valid", false);
                if (!epcOk)
                {
                    string err = SimpleJson.ExtractString(valid.Body, "error");
                    BeginInvoke(new EventHandler(delegate
                    {
                        _status.Text = MerlinUi.ShortLine(err.Length > 0 ? err : "EPC in use", 80);
                    }), null, EventArgs.Empty);
                    return;
                }

                string cat = _lookupCategory ?? "";
                HttpResult res = _api.RegisterItem(epc, name, cat, _upcBox.Text.Trim(), homeBin);
                BeginInvoke(new EventHandler(delegate
                {
                    if (res.Ok)
                    {
                        _status.Text = MerlinUi.ShortLine("OK " + name, 80);
                        var added = new ItemInfo();
                        added.EpcId = epc;
                        added.Name = name;
                        added.Category = cat;
                        added.ContainerId = homeBin;
                        var bin = _state.FindBin(homeBin);
                        if (bin != null) added.ContainerName = bin.Name;
                        _state.Items.Add(added);
                        _lookupCategory = "";
                        _epcBox.Text = "";
                        _upcBox.Text = "";
                        _nameBox.Text = "";
                    }
                    else
                    {
                        string err = SimpleJson.ExtractString(res.Body, "error");
                        _status.Text = MerlinUi.ShortLine(err.Length > 0 ? err : res.Error, 80);
                    }
                }), null, EventArgs.Empty);
            });
        }
    }

    public class SettingsPanel : UserControl
    {
        private readonly AppConfig _cfg;
        private readonly HandheldState _state;
        private readonly InventoryApiClient _api;
        private readonly TextBox _serverBox;
        private readonly TextBox _scannerBox;
        private readonly Label _status;
        private readonly Label _versionLabel;

        public SettingsPanel(AppConfig cfg, HandheldState state, InventoryApiClient api)
        {
            _cfg = cfg;
            _state = state;
            _api = api;
            MerlinUi.StylePanel(this);

            _status = MerlinUi.MakeStatusLabel();
            _status.Height = 48;
            _status.Text = _state.LastMessage;

            var syncBtn = MerlinUi.MakePrimaryButton("Sync inventory");
            syncBtn.Click += delegate { Sync(); };

            var huntBtn = MerlinUi.MakeButton("Refresh hunt only");
            huntBtn.Click += delegate { HuntSync(); };

            var exitBtn = MerlinUi.MakeButton("Exit app");
            exitBtn.Click += delegate { RequestExit(); };

            var versionBtn = MerlinUi.MakeButton("Check git version");
            versionBtn.Click += delegate { CheckGitVersion(true); };

            var pingBtn = MerlinUi.MakeButton("Test ping");
            pingBtn.Click += delegate { Ping(); };

            var saveBtn = MerlinUi.MakeButton("Save settings");
            saveBtn.Click += delegate { SaveSettings(); };

            _scannerBox = MerlinUi.MakeField();
            _scannerBox.Text = _cfg.ScannerId;

            _serverBox = MerlinUi.MakeField();
            _serverBox.Text = _cfg.ServerUrl;

            _versionLabel = new Label
            {
                Text = AppVersionInfo.FormatInstalledLabel(),
                Height = 28,
                Dock = DockStyle.Top,
                Font = MerlinUi.FontSmBold,
                ForeColor = MerlinUi.Accent,
            };

            Controls.Add(_status);
            Controls.Add(exitBtn);
            Controls.Add(syncBtn);
            Controls.Add(huntBtn);
            Controls.Add(versionBtn);
            Controls.Add(pingBtn);
            Controls.Add(saveBtn);
            Controls.Add(_scannerBox);
            Controls.Add(MerlinUi.MakeCaption("Scanner ID"));
            Controls.Add(_serverBox);
            Controls.Add(MerlinUi.MakeCaption("Server"));
            Controls.Add(_versionLabel);
        }

        public event EventHandler HuntSyncCompleted;
        public event EventHandler SyncCompleted;
        public event EventHandler AppExitRequested;
        public event EventHandler VersionCheckCompleted;

        private void RequestExit()
        {
            if (AppExitRequested != null) AppExitRequested(this, EventArgs.Empty);
        }

        private void CheckGitVersion(bool promptIfNew)
        {
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.Save();
            _status.Text = "Checking git…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult upd = UpdateChecker.Check(_api, AppConfig.AppVersion);
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = MerlinUi.ShortLine(UpdateChecker.DescribeForUi(upd), 90);
                    _versionLabel.Text = AppVersionInfo.FormatInstalledLabel();
                    if (upd.Reachable && upd.ServerVersion.Length > 0)
                    {
                        _versionLabel.Text = "Here: " + AppConfig.AppVersion + "\r\nSrv: " + upd.ServerVersion;
                    }
                    if (promptIfNew && upd.UpdateAvailable)
                    {
                        UpdateChecker.PromptIfUpdateAvailable(upd, _cfg.ServerUrl);
                    }
                    if (VersionCheckCompleted != null) VersionCheckCompleted(this, EventArgs.Empty);
                }), null, EventArgs.Empty);
            });
        }

        private void SaveSettings()
        {
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.ScannerId = _scannerBox.Text.Trim();
            _cfg.Save();
            _status.Text = "Saved";
        }

        private void Ping()
        {
            _status.Text = "Ping…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.Ping();
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = res.Ok ? "Ping OK" : MerlinUi.ShortLine(res.Error, 80);
                }), null, EventArgs.Empty);
            });
        }

        private void HuntSync()
        {
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.ScannerId = _scannerBox.Text.Trim();
            _cfg.Save();
            _status.Text = "Hunt sync…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.SyncSummary();
                string err = "";
                bool ok = res.Ok && _api.TryApplyHuntSummary(res.Body, _state, out err);
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = ok ? MerlinUi.ShortLine(_state.LastMessage, 80) : MerlinUi.ShortLine(err, 80);
                    if (ok && HuntSyncCompleted != null) HuntSyncCompleted(this, EventArgs.Empty);
                }), null, EventArgs.Empty);
            });
        }

        private void Sync()
        {
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.ScannerId = _scannerBox.Text.Trim();
            _cfg.Save();
            _status.Text = "Syncing…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                _api.ScannerPing();
                HttpResult res = _api.FullSync();
                string err = "";
                bool ok = res.Ok && _api.TryApplyFullSync(res.Body, _state, out err);
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = ok ? MerlinUi.ShortLine(_state.LastMessage, 80) : MerlinUi.ShortLine(err, 80);
                    if (SyncCompleted != null) SyncCompleted(this, EventArgs.Empty);
                }), null, EventArgs.Empty);
            });
        }
    }
}
