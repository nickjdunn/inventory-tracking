using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MerlinAudit
{
    public class MainForm : Form
    {
        private readonly AuditConfig _cfg = AuditConfig.Load();
        private readonly AuditClient _client;
        private readonly Label _status;
        private readonly Label _pageTitle;
        private TextBox _logBox;
        private TextBox _serverBox;
        private TextBox _scannerBox;

        private Panel _contentHost;
        private Panel[] _pages;
        private Button[][] _hotkeys;
        private EventHandler[][] _keyHandlers;
        private int _pageIndex;
        private string _lastReportJson = "";
        private string _lastScanSessionJson = "";

        public int ContentWidth
        {
            get
            {
                if (_contentHost == null) return 228;
                int w = _contentHost.ClientSize.Width;
                return w > 0 ? w : 228;
            }
        }

        public int ContentHeight
        {
            get
            {
                if (_contentHost == null) return 200;
                int h = _contentHost.ClientSize.Height;
                return h > 0 ? h : 200;
            }
        }

        private static readonly string[] PageNames = new string[]
        {
            "Tests",
            "More",
            "Settings",
            "Log",
        };

        public MainForm()
        {
            _client = new AuditClient(_cfg);
            UiTheme.ApplyForm(this);
            Text = "Merlin Lab " + AuditConfig.AppVersion;
            Width = 240;
            Height = 320;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            _status = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                TextAlign = ContentAlignment.TopLeft,
                Left = 6,
                ForeColor = UiTheme.Text,
                BackColor = UiTheme.Card,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                Text = "Ready",
            };

            _pageTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                TextAlign = ContentAlignment.TopLeft,
                Left = 6,
                ForeColor = UiTheme.Accent,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
            };

            _contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Bg,
            };

            var nav = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = UiTheme.Card,
            };

            var btnPrev = UiTheme.MakeNavButton("<");
            btnPrev.Left = 4;
            btnPrev.Top = 2;
            btnPrev.Click += delegate { ShowPage(_pageIndex - 1); };

            var btnNext = UiTheme.MakeNavButton(">");
            btnNext.Left = 200;
            btnNext.Top = 2;
            btnNext.Click += delegate { ShowPage(_pageIndex + 1); };

            var pageIndicator = UiTheme.MakeHint("Keys 1-9 on this page");
            pageIndicator.Dock = DockStyle.None;
            CfLayout.Place(pageIndicator, 44, 8, 152, 14);
            pageIndicator.TextAlign = ContentAlignment.TopCenter;

            nav.Controls.Add(pageIndicator);
            nav.Controls.Add(btnNext);
            nav.Controls.Add(btnPrev);

            BuildPages(_contentHost);

            Controls.Add(nav);
            Controls.Add(_contentHost);
            Controls.Add(_pageTitle);
            Controls.Add(_status);

            _contentHost.Resize += delegate
            {
                LayoutVisiblePage();
                if (_pages != null && _pageIndex >= 0 && _pageIndex < _pages.Length)
                {
                    PageLayout.Apply(this, _pageIndex, _pages[_pageIndex], _hotkeys[_pageIndex], _logBox);
                }
            };

            KeyDown += MainForm_KeyDown;
            Load += delegate
            {
                ShowPage(0);
                RunPing();
                FlushPendingErrorFromDisk();
                RefreshLogPreview();
            };
        }

        private void BuildPages(Panel host)
        {
            _pages = new Panel[4];
            _hotkeys = new Button[4][];
            _keyHandlers = new EventHandler[4][];

            _pages[0] = BuildTestsPage1();
            _pages[1] = BuildTestsPage2();
            _pages[2] = BuildSettingsPage();
            _pages[3] = BuildLogPage();

            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = false;
                host.Controls.Add(_pages[i]);
            }
        }

        private void LayoutVisiblePage()
        {
            if (_pages == null || _contentHost == null) return;
            int w = _contentHost.ClientSize.Width;
            int h = _contentHost.ClientSize.Height;
            if (w < 50) w = 228;
            if (h < 50) h = 200;

            for (int i = 0; i < _pages.Length; i++)
            {
                CfLayout.Place(_pages[i], 0, 0, w, h);
            }
        }

        private Panel BuildTestsPage1()
        {
            var p = NewPage();
            var hint = UiTheme.MakeHint("Scanner tests — press number");
            CfLayout.Place(hint, 6, 2, 228, 14);
            p.Controls.Add(hint);

            _hotkeys[0] = new Button[4];
            string[] labels0 = new string[] { "Scan guide", "RSSI track", "Wedge probe", "Trigger / NUR" };
            _keyHandlers[0] = new EventHandler[4];
            _keyHandlers[0][0] = delegate { RunScanGuide(); };
            _keyHandlers[0][1] = delegate { OpenLab(new RssiTrackForm(_cfg)); };
            _keyHandlers[0][2] = delegate { OpenLab(new WedgeProbeForm(_cfg)); };
            _keyHandlers[0][3] = delegate { OpenLab(new TriggerProbeForm(_cfg)); };
            for (int i = 0; i < 4; i++)
            {
                _hotkeys[0][i] = AddHotkey(p, i + 1, labels0[i], 20 + i * 36, i < 2, _keyHandlers[0][i]);
            }
            return p;
        }

        private Panel BuildTestsPage2()
        {
            var p = NewPage();
            var hint = UiTheme.MakeHint("Audit & upload");
            CfLayout.Place(hint, 6, 2, 228, 14);
            p.Controls.Add(hint);

            _hotkeys[1] = new Button[4];
            _keyHandlers[1] = new EventHandler[4];
            _keyHandlers[1][0] = delegate { RunFullAudit(true); };
            _keyHandlers[1][1] = delegate { RunFullAudit(false); };
            _keyHandlers[1][2] = delegate { ReuploadLast(); };
            _keyHandlers[1][3] = delegate { UploadLabLog(); };
            string[] labels1 = new string[]
            {
                "Full audit + upload", "Inventory only", "Re-upload audit", "Upload lab log",
            };
            for (int i = 0; i < 4; i++)
            {
                _hotkeys[1][i] = AddHotkey(p, i + 1, labels1[i], 20 + i * 36, i == 0 || i == 3, _keyHandlers[1][i]);
            }
            return p;
        }

        private Panel BuildSettingsPage()
        {
            var p = NewPage();
            _hotkeys[2] = new Button[2];

            var hdr = UiTheme.MakeHeader("Server settings");
            CfLayout.Place(hdr, 6, 4, 228, 18);
            p.Controls.Add(hdr);

            var lblSrv = UiTheme.MakeHint("Server URL");
            CfLayout.Place(lblSrv, 6, 26, 228, 12);
            p.Controls.Add(lblSrv);

            _serverBox = UiTheme.MakeField(_cfg.ServerUrl);
            CfLayout.Place(_serverBox, 6, 40, 228, 22);
            p.Controls.Add(_serverBox);

            var lblId = UiTheme.MakeHint("Scanner ID");
            CfLayout.Place(lblId, 6, 66, 228, 12);
            p.Controls.Add(lblId);

            _scannerBox = UiTheme.MakeField(_cfg.ScannerId);
            CfLayout.Place(_scannerBox, 6, 80, 228, 22);
            p.Controls.Add(_scannerBox);

            var info = UiTheme.MakeHint("PC: /deploy/device-audit.html");
            CfLayout.Place(info, 6, 108, 228, 12);
            p.Controls.Add(info);

            _keyHandlers[2] = new EventHandler[2];
            _keyHandlers[2][0] = delegate { RunPing(); };
            _keyHandlers[2][1] = delegate { SaveSettings(); };
            _hotkeys[2][0] = AddHotkey(p, 1, "Ping server", 196, true, _keyHandlers[2][0]);
            _hotkeys[2][1] = AddHotkey(p, 2, "Save settings", 196, false, _keyHandlers[2][1]);
            CfLayout.Place(_hotkeys[2][0], 6, 196, 110, 34);
            CfLayout.Place(_hotkeys[2][1], 124, 196, 110, 34);

            return p;
        }

        private Panel BuildLogPage()
        {
            var p = NewPage();

            var hdr = UiTheme.MakeHeader("Session log");
            CfLayout.Place(hdr, 6, 2, 228, 16);
            p.Controls.Add(hdr);

            _logBox = UiTheme.MakeLogBox();
            CfLayout.Place(_logBox, 6, 20, 228, 168);
            p.Controls.Add(_logBox);

            _hotkeys[3] = new Button[2];
            _keyHandlers[3] = new EventHandler[2];
            _keyHandlers[3][0] = delegate { UploadLabLog(); };
            _keyHandlers[3][1] = delegate { ClearLabLog(); };
            _hotkeys[3][0] = AddHotkey(p, 1, "Upload lab log", 194, true, _keyHandlers[3][0]);
            _hotkeys[3][1] = AddHotkey(p, 2, "Clear lab log", 194, false, _keyHandlers[3][1]);
            CfLayout.Place(_hotkeys[3][0], 6, 194, 110, 32);
            CfLayout.Place(_hotkeys[3][1], 124, 194, 110, 32);

            return p;
        }

        private static Panel NewPage()
        {
            return new Panel { BackColor = UiTheme.Bg };
        }

        private static Button AddHotkey(Panel p, int num, string label, int top, bool primary, EventHandler click)
        {
            var b = UiTheme.MakeHotkeyButton(num, label, primary);
            b.Left = 6;
            b.Top = top;
            b.Width = 228;
            b.Height = 34;
            b.Visible = true;
            if (click != null) b.Click += click;
            p.Controls.Add(b);
            b.BringToFront();
            return b;
        }

        private void ShowPage(int index)
        {
            if (_pages == null || _contentHost == null || _pageTitle == null) return;
            if (index < 0) index = _pages.Length - 1;
            if (index >= _pages.Length) index = 0;
            _pageIndex = index;

            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = (i == _pageIndex);
            }
            _pages[_pageIndex].BringToFront();
            LayoutVisiblePage();
            PageLayout.Apply(this, _pageIndex, _pages[_pageIndex], _hotkeys[_pageIndex], _logBox);

            _pageTitle.Text = PageNames[_pageIndex] + "  (" + (_pageIndex + 1) + "/" + _pages.Length + ")";
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { ShowPage(_pageIndex - 1); e.Handled = true; return; }
            if (e.KeyCode == Keys.Right) { ShowPage(_pageIndex + 1); e.Handled = true; return; }

            int digit = KeyToDigit(e.KeyCode);
            if (digit < 1) return;
            if (_keyHandlers == null || _pageIndex < 0 || _pageIndex >= _keyHandlers.Length) return;
            EventHandler[] handlers = _keyHandlers[_pageIndex];
            if (handlers == null || digit > handlers.Length) return;
            EventHandler h = handlers[digit - 1];
            if (h != null) h(this, EventArgs.Empty);
            e.Handled = true;
        }

        private static int KeyToDigit(Keys key)
        {
            if (key >= Keys.D1 && key <= Keys.D9) return (int)key - (int)Keys.D0;
            if (key >= Keys.NumPad1 && key <= Keys.NumPad9) return (int)key - (int)Keys.NumPad0;
            return 0;
        }

        private void OpenLab(Form f)
        {
            SaveSettings();
            f.ShowDialog();
            RefreshLogPreview();
        }

        private void RefreshLogPreview()
        {
            if (_logBox == null) return;
            string lab = TestSessionLog.SummaryText;
            _logBox.Text = lab.Length > 0
                ? lab
                : "Lab events appear here.\r\nRun RSSI, wedge, or trigger tests.";
        }

        private void ClearLabLog()
        {
            TestSessionLog.Clear();
            RefreshLogPreview();
            _status.Text = "Lab log cleared";
        }

        private void UploadLabLog()
        {
            SaveSettings();
            string json = TestSessionLog.ToJson();
            if (json.IndexOf("\"events\":[]") >= 0)
            {
                _status.Text = "No lab events";
                return;
            }
            AuditLocalLabStore.SaveSnapshot(json);
            _status.Text = "Uploading lab…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _client.UploadLabSession(json);
                BeginInvoke(new EventHandler(delegate
                {
                    if (res.Ok)
                    {
                        AuditLocalLabStore.Clear();
                        _status.Text = "Lab log on server";
                        RefreshLogPreview();
                    }
                    else
                    {
                        AuditPendingQueue.MarkLabSnapshot();
                        _status.Text = Short(res.Error, 50);
                    }
                }), null, EventArgs.Empty);
            });
        }

        private void FlushPendingErrorFromDisk()
        {
            string note = PendingRecovery.FlushAll(_cfg, _client);
            if (note.Length > 0)
            {
                _status.Text = Short(note.Replace("\r\n", " "), 58);
                if (note.IndexOf("pending") >= 0 || note.IndexOf("uploaded") >= 0)
                {
                    PendingRecovery.NotifyUser(note + "\r\n\r\nPC: device-audit.html");
                }
            }
        }

        private void SaveSettings()
        {
            if (_serverBox != null) _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            if (_scannerBox != null) _cfg.ScannerId = _scannerBox.Text.Trim();
            _cfg.Save();
            _status.Text = "Settings saved";
        }

        private void RunPing()
        {
            SaveSettings();
            _status.Text = "Ping…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _client.Ping();
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = res.Ok ? "Ping OK" : ("Ping fail: " + Short(res.Error, 40));
                }), null, EventArgs.Empty);
            });
        }

        private void RunScanGuide()
        {
            SaveSettings();
            try
            {
                var guide = new GuidedScanForm(_cfg);
                guide.ShowDialog();
                _lastScanSessionJson = guide.SessionJson;
                TestSessionLog.Add("scan_guide", guide.Completed ? "complete" : "partial", _lastScanSessionJson);
                RefreshLogPreview();
                _status.Text = guide.Completed ? "Guide done" : "Guide partial";
            }
            catch (Exception ex)
            {
                AuditErrorReporter.ReportSync(_cfg, "scan_guide", ex, "");
                _status.Text = "Guide error (logged)";
            }
        }

        private void RunFullAudit(bool upload)
        {
            SaveSettings();
            _status.Text = "Scan guide…";
            var guide = new GuidedScanForm(_cfg);
            guide.ShowDialog();
            _lastScanSessionJson = guide.SessionJson;

            _status.Text = "Collecting…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string reportJson = "";
                string err = "";
                try
                {
                    var collector = new DeviceAuditCollector(_cfg);
                    reportJson = collector.CollectReportJson(_lastScanSessionJson);
                }
                catch (Exception ex)
                {
                    err = ex.Message ?? "collect failed";
                }

                HttpResult uploadRes = null;
                if (err.Length == 0 && upload)
                {
                    uploadRes = _client.UploadReport(reportJson);
                    if (!uploadRes.Ok) err = uploadRes.Error ?? "upload failed";
                }

                BeginInvoke(new EventHandler(delegate
                {
                    if (err.Length > 0)
                    {
                        _status.Text = Short(err, 60);
                        return;
                    }
                    _lastReportJson = reportJson;
                    if (upload && uploadRes != null)
                    {
                        string id = SimpleJson.ExtractString(uploadRes.Body, "id");
                        _status.Text = id.Length > 0 ? ("Uploaded " + id) : "Uploaded OK";
                    }
                    else
                    {
                        _status.Text = "Audit done (local)";
                    }
                    RefreshLogPreview();
                }), null, EventArgs.Empty);
            });
        }

        private void ReuploadLast()
        {
            if (_lastReportJson == null || _lastReportJson.Length == 0)
            {
                _status.Text = "Run full audit first";
                return;
            }
            SaveSettings();
            _status.Text = "Uploading…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _client.UploadReport(_lastReportJson);
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = res.Ok ? "Re-upload OK" : Short(res.Error, 50);
                }), null, EventArgs.Empty);
            });
        }

        private static string Short(string text, int max)
        {
            if (text == null) return "";
            text = text.Trim();
            if (text.Length <= max) return text;
            return text.Substring(0, max - 1) + "~";
        }
    }
}
