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
        public event EventHandler StatusChanged;

        public ReceivePanel(AppConfig cfg, HandheldState state, InventoryApiClient api)
        {
            _cfg = cfg;
            _state = state;
            _api = api;

            var hint = new Label
            {
                Text = "Trigger / F1 = RFID wedge or NUR. Paste tags below if needed.",
                Top = 4,
                Left = 4,
                Width = 300,
                Height = 28,
                Font = new Font("Tahoma", 8f, FontStyle.Regular)
            };

            var binLbl = new Label { Text = "Target bin", Top = 34, Left = 4, Width = 80, Height = 16 };
            _binCombo = new ComboBox { Top = 52, Left = 4, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };

            var sendBtn = new Button
            {
                Text = "Send tags (Trigger)",
                Top = 82,
                Left = 4,
                Width = 300,
                Height = 36,
                BackColor = Color.FromArgb(3, 105, 161),
                ForeColor = Color.White
            };
            sendBtn.Click += delegate { SendTags(); };

            _tagsBox = new TextBox
            {
                Top = 124,
                Left = 4,
                Width = 300,
                Height = 120,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Tahoma", 8f, FontStyle.Regular)
            };

            _resultLabel = new Label
            {
                Text = "Ready",
                Top = 250,
                Left = 4,
                Width = 300,
                Height = 60,
                Font = new Font("Tahoma", 8f, FontStyle.Regular)
            };

            Controls.Add(hint);
            Controls.Add(binLbl);
            Controls.Add(_binCombo);
            Controls.Add(sendBtn);
            Controls.Add(_tagsBox);
            Controls.Add(_resultLabel);
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
            if (text != null) _tagsBox.Text = text;
        }

        private void SendTags()
        {
            if (_binCombo.SelectedItem == null)
            {
                _resultLabel.Text = "Select a bin first";
                return;
            }
            var bin = (BinInfo)_binCombo.SelectedItem;
            _cfg.LastBinId = bin.Id;
            _cfg.Save();

            ArrayList tags = TagParser.ParseText(_tagsBox.Text);
            if (tags.Count == 0)
            {
                _resultLabel.Text = "No tags to send";
                return;
            }

            _resultLabel.Text = "Sending " + tags.Count + " tags…";
            string binId = bin.Id;
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.PostScan(tags, binId);
                string summary = res.Ok
                    ? InventoryApiClient.FormatScanResult(res.Body)
                    : (res.Error.Length > 0 ? res.Error : res.Body);
                BeginInvoke(new EventHandler(delegate
                {
                    _resultLabel.Text = summary;
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

        public FindPanel(AppConfig cfg, HandheldState state, InventoryApiClient api)
        {
            _cfg = cfg;
            _state = state;
            _api = api;

            var refreshHuntBtn = new Button
            {
                Text = "Refresh hunt",
                Top = 4,
                Left = 4,
                Width = 300,
                Height = 26
            };
            refreshHuntBtn.Click += delegate { RefreshHuntFromServer(); };

            _searchBox = new TextBox { Top = 34, Left = 4, Width = 300 };
            _searchBox.TextChanged += delegate { RefreshList(); };

            _list = new ListBox { Top = 58, Left = 4, Width = 300, Height = 122, Font = new Font("Tahoma", 8f, FontStyle.Regular) };

            var huntBtn = new Button
            {
                Text = "Hunt selected",
                Top = 186,
                Left = 4,
                Width = 300,
                Height = 32
            };
            huntBtn.Click += delegate { StartHunt(); };

            var clearBtn = new Button
            {
                Text = "Clear hunt",
                Top = 222,
                Left = 4,
                Width = 140,
                Height = 28
            };
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

            _huntLabel = new Label
            {
                Text = "Sync inventory on Settings tab",
                Top = 256,
                Left = 4,
                Width = 300,
                Height = 50,
                Font = new Font("Tahoma", 8f, FontStyle.Regular)
            };

            Controls.Add(refreshHuntBtn);
            Controls.Add(_searchBox);
            Controls.Add(_list);
            Controls.Add(huntBtn);
            Controls.Add(clearBtn);
            Controls.Add(_huntLabel);
        }

        public void RefreshHuntDisplay()
        {
            if (_state.HuntQueue.Count == 0)
            {
                _huntLabel.Text = "No hunt targets — select item and Hunt";
                _huntLabel.ForeColor = Color.White;
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append("Hunting ");
            sb.Append(_state.HuntQueue.Count);
            sb.Append(": ");
            for (int i = 0; i < _state.HuntQueue.Count && i < 2; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append((string)_state.HuntQueue[i]);
            }
            if (_state.HuntQueue.Count > 2) sb.Append("…");
            _huntLabel.Text = sb.ToString();
            _huntLabel.ForeColor = Color.Khaki;
        }

        public void RefreshHuntFromServer()
        {
            _huntLabel.Text = "Refreshing hunt…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.SyncSummary();
                string err = "";
                bool ok = res.Ok && _api.TryApplyHuntSummary(res.Body, _state, out err);
                BeginInvoke(new EventHandler(delegate
                {
                    if (ok)
                    {
                        RefreshHuntDisplay();
                    }
                    else
                    {
                        _huntLabel.Text = err.Length > 0 ? err : res.Error;
                    }
                }), null, EventArgs.Empty);
            });
        }

        public void RefreshList()
        {
            _list.Items.Clear();
            ArrayList filtered = _state.FilterItems(_searchBox.Text);
            for (int i = 0; i < filtered.Count; i++)
            {
                var it = (ItemInfo)filtered[i];
                _list.Items.Add(it);
            }
            _list.DisplayMember = "ListLine";
        }

        public void OnTriggerRead(string wedgeText)
        {
            if (wedgeText == null || wedgeText.Length == 0) return;
            ArrayList tags = TagParser.ParseText(wedgeText);
            if (tags.Count == 0) return;
            string epc = ((TagRead)tags[0]).Epc;
            ItemInfo hit = _state.FindItem(epc);

            if (_state.HuntQueue.Count > 0)
            {
                _huntLabel.Text = "Sending RSSI…";
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
                _huntLabel.Text = "MATCH: " + hit.ListLine;
                _huntLabel.ForeColor = Color.LightGreen;
                if (huntSignal.Length > 0) _huntLabel.Text += " — " + huntSignal;
                return;
            }
            if (_state.HuntQueue.Count > 0)
            {
                bool inQueue = false;
                for (int i = 0; i < _state.HuntQueue.Count; i++)
                {
                    if (string.Compare((string)_state.HuntQueue[i], epc, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        inQueue = true;
                        break;
                    }
                }
                if (huntSignal.Length > 0)
                {
                    _huntLabel.Text = (inQueue ? "Hunt: " : "Read: ") + epc + " — " + huntSignal;
                    _huntLabel.ForeColor = inQueue ? Color.Khaki : Color.Salmon;
                }
                else
                {
                    _huntLabel.Text = inQueue ? "Hunt target: " + epc : "Not in hunt queue: " + epc;
                    _huntLabel.ForeColor = inQueue ? Color.Khaki : Color.Salmon;
                }
                return;
            }
            _huntLabel.Text = huntSignal.Length > 0 ? ("Read: " + epc + " — " + huntSignal) : ("Read: " + epc);
            _huntLabel.ForeColor = Color.White;
        }

        private void StartHunt()
        {
            if (_list.SelectedItem == null)
            {
                _huntLabel.Text = "Select an item";
                return;
            }
            var it = (ItemInfo)_list.SelectedItem;
            var queue = new ArrayList();
            queue.Add(it.EpcId);
            _huntLabel.Text = "Starting hunt…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.PostHuntQueue(queue);
                BeginInvoke(new EventHandler(delegate
                {
                    if (res.Ok)
                    {
                        _state.HuntQueue.Clear();
                        _state.HuntQueue.Add(it.EpcId);
                        _huntLabel.Text = "Hunting: " + it.ListLine + " (pull trigger for RSSI)";
                        _huntLabel.ForeColor = Color.Khaki;
                    }
                    else
                    {
                        _huntLabel.Text = "Hunt failed: " + res.Error;
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

            Controls.Add(new Label { Text = "Scan key / F2 = UPC wedge", Top = 4, Left = 4, Width = 200, Height = 16 });

            _upcBox = new TextBox { Top = 22, Left = 4, Width = 200 };
            var lookupBtn = new Button { Text = "Lookup", Top = 22, Left = 210, Width = 90, Height = 22 };
            lookupBtn.Click += delegate { LookupUpc(); };

            Controls.Add(new Label { Text = "Name", Top = 50, Left = 4, Width = 60, Height = 16 });
            _nameBox = new TextBox { Top = 68, Left = 4, Width = 296 };

            Controls.Add(new Label { Text = "EPC (Trigger)", Top = 94, Left = 4, Width = 120, Height = 16 });
            _epcBox = new TextBox { Top = 112, Left = 4, Width = 296 };

            Controls.Add(new Label { Text = "Home bin", Top = 138, Left = 4, Width = 80, Height = 16 });
            _binCombo = new ComboBox { Top = 156, Left = 4, Width = 296, DropDownStyle = ComboBoxStyle.DropDownList };

            var saveBtn = new Button
            {
                Text = "Register item",
                Top = 188,
                Left = 4,
                Width = 296,
                Height = 36,
                BackColor = Color.FromArgb(3, 105, 161),
                ForeColor = Color.White
            };
            saveBtn.Click += delegate { Register(); };

            _status = new Label { Top = 230, Left = 4, Width = 296, Height = 70, Font = new Font("Tahoma", 8f, FontStyle.Regular) };

            Controls.Add(_upcBox);
            Controls.Add(lookupBtn);
            Controls.Add(_nameBox);
            Controls.Add(_epcBox);
            Controls.Add(_binCombo);
            Controls.Add(saveBtn);
            Controls.Add(_status);
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
            _status.Text = "Looking up UPC…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.LookupUpc(upc);
                BeginInvoke(new EventHandler(delegate
                {
                    if (!res.Ok)
                    {
                        _status.Text = "Lookup failed: " + res.Error;
                        return;
                    }
                    bool found = SimpleJson.ExtractBool(res.Body, "found", false);
                    if (!found)
                    {
                        _status.Text = "UPC not found — enter name manually";
                        return;
                    }
                    string title = SimpleJson.ExtractString(res.Body, "name");
                    if (title.Length == 0) title = SimpleJson.ExtractString(res.Body, "title");
                    _nameBox.Text = title;
                    string cat = SimpleJson.ExtractString(res.Body, "category");
                    _lookupCategory = cat;
                    _status.Text = "Found: " + title + (cat.Length > 0 ? " (" + cat + ")" : "");
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
                _status.Text = "EPC required — pull Trigger";
                return;
            }

            string homeBin = "";
            if (_binCombo.SelectedItem != null) homeBin = ((BinInfo)_binCombo.SelectedItem).Id;

            _status.Text = "Validating EPC…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult valid = _api.ValidateEpc(epc);
                bool epcOk = valid.Ok && SimpleJson.ExtractBool(valid.Body, "valid", false);
                if (!epcOk)
                {
                    string err = SimpleJson.ExtractString(valid.Body, "error");
                    BeginInvoke(new EventHandler(delegate
                    {
                        _status.Text = err.Length > 0 ? err : "EPC not available";
                    }), null, EventArgs.Empty);
                    return;
                }

                string cat = _lookupCategory ?? "";
                HttpResult res = _api.RegisterItem(epc, name, cat, _upcBox.Text.Trim(), homeBin);
                BeginInvoke(new EventHandler(delegate
                {
                    if (res.Ok)
                    {
                        _status.Text = "Registered " + name;
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
                        _status.Text = err.Length > 0 ? err : res.Error;
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

            _versionLabel = new Label
            {
                Text = "App v" + AppConfig.AppVersion,
                Top = 4,
                Left = 4,
                Width = 296,
                Height = 16,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248)
            };
            Controls.Add(_versionLabel);

            Controls.Add(new Label { Text = "Server URL", Top = 22, Left = 4, Width = 120, Height = 16 });
            _serverBox = new TextBox { Top = 40, Left = 4, Width = 296, Text = _cfg.ServerUrl };

            Controls.Add(new Label { Text = "Scanner ID", Top = 66, Left = 4, Width = 120, Height = 16 });
            _scannerBox = new TextBox { Top = 84, Left = 4, Width = 296, Text = _cfg.ScannerId };

            var saveBtn = new Button { Text = "Save", Top = 112, Left = 4, Width = 90, Height = 28 };
            saveBtn.Click += delegate { SaveSettings(); };

            var pingBtn = new Button { Text = "Test ping", Top = 112, Left = 100, Width = 90, Height = 28 };
            pingBtn.Click += delegate { Ping(); };

            var updateBtn = new Button { Text = "Check updates", Top = 112, Left = 196, Width = 104, Height = 28 };
            updateBtn.Click += delegate { CheckUpdates(); };

            var huntBtn = new Button
            {
                Text = "Refresh hunt only",
                Top = 146,
                Left = 4,
                Width = 296,
                Height = 28
            };
            huntBtn.Click += delegate { HuntSync(); };

            var syncBtn = new Button
            {
                Text = "Sync full inventory",
                Top = 178,
                Left = 4,
                Width = 296,
                Height = 32,
                BackColor = Color.FromArgb(3, 105, 161),
                ForeColor = Color.White
            };
            syncBtn.Click += delegate { Sync(); };

            _status = new Label
            {
                Text = _state.LastMessage,
                Top = 216,
                Left = 4,
                Width = 296,
                Height = 80,
                Font = new Font("Tahoma", 8f, FontStyle.Regular)
            };

            Controls.Add(_serverBox);
            Controls.Add(_scannerBox);
            Controls.Add(saveBtn);
            Controls.Add(pingBtn);
            Controls.Add(updateBtn);
            Controls.Add(huntBtn);
            Controls.Add(syncBtn);
            Controls.Add(_status);
        }

        public event EventHandler HuntSyncCompleted;

        private void CheckUpdates()
        {
            _status.Text = "Checking for updates…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult upd = UpdateChecker.Check(_api, AppConfig.AppVersion);
                BeginInvoke(new EventHandler(delegate
                {
                    if (upd.UpdateAvailable)
                    {
                        _status.Text = "Update " + upd.ServerVersion + " ready";
                        UpdateChecker.PromptIfUpdateAvailable(upd, _cfg.ServerUrl);
                    }
                    else if (upd.ServerVersion.Length > 0)
                    {
                        _status.Text = "Up to date (" + AppConfig.AppVersion + ")";
                    }
                    else
                    {
                        _status.Text = "Could not reach deploy info";
                    }
                }), null, EventArgs.Empty);
            });
        }

        private void SaveSettings()
        {
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.ScannerId = _scannerBox.Text.Trim();
            _cfg.Save();
            _status.Text = "Settings saved";
        }

        private void Ping()
        {
            _status.Text = "Ping…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.Ping();
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = res.Ok ? "Ping OK" : ("Ping failed: " + res.Error);
                }), null, EventArgs.Empty);
            });
        }

        public event EventHandler SyncCompleted;

        private void HuntSync()
        {
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.ScannerId = _scannerBox.Text.Trim();
            _cfg.Save();
            _status.Text = "Refreshing hunt…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.SyncSummary();
                string err = "";
                bool ok = res.Ok && _api.TryApplyHuntSummary(res.Body, _state, out err);
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = ok ? _state.LastMessage : (err.Length > 0 ? err : res.Error);
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
                    _status.Text = ok ? _state.LastMessage : (err.Length > 0 ? err : res.Error);
                    if (SyncCompleted != null) SyncCompleted(this, EventArgs.Empty);
                }), null, EventArgs.Empty);
            });
        }
    }
}
