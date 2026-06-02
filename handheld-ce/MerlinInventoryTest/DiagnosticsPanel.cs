using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MerlinHandheld
{
    public class DiagnosticsPanel : UserControl
    {
        private readonly AppConfig _cfg;
        private readonly InventoryApiClient _api;
        private readonly CheckBox _chkLiveRaw;
        private readonly CheckBox _chkLiveScan;
        private readonly CheckBox _chkDiagLog;
        private readonly TextBox _liveBinBox;
        private readonly TextBox _previewBox;
        private readonly Label _status;
        private readonly Label _urlLabel;
        private readonly System.Windows.Forms.Timer _previewTimer;

        public DiagnosticsPanel(AppConfig cfg, InventoryApiClient api)
        {
            _cfg = cfg;
            _api = api;
            MerlinUi.StylePanel(this);
            MerlinUi.EnablePanelScroll(this);

            _status = MerlinUi.MakeStatusLabel();
            _status.Height = 36;

            _previewBox = MerlinUi.MakeField();
            _previewBox.Multiline = true;
            _previewBox.Height = 56;
            _previewBox.ScrollBars = ScrollBars.Vertical;
            _previewBox.ReadOnly = true;
            _previewBox.Font = new Font("Tahoma", 7f, FontStyle.Regular);

            _chkLiveRaw = MakeCheck("Live raw → server", _cfg.LiveRawStream);
            _chkLiveScan = MakeCheck("Live scan → inventory", _cfg.LiveScanStream);
            _chkDiagLog = MakeCheck("Trace → browser (all tabs)", _cfg.DiagnosticLogFile);
            _chkDiagLog.CheckStateChanged += DiagLog_CheckedChanged;

            _liveBinBox = MerlinUi.MakeField();
            _liveBinBox.Text = _cfg.LiveScanBinId != null && _cfg.LiveScanBinId.Length > 0
                ? _cfg.LiveScanBinId
                : (_cfg.LastBinId ?? "");

            _urlLabel = new Label
            {
                Height = 28,
                Dock = DockStyle.Top,
                ForeColor = MerlinUi.Accent,
                Font = MerlinUi.FontSm,
                Text = "Browser live view:",
            };

            var saveBtn = MerlinUi.MakePrimaryButton("Save stream opts");
            saveBtn.Click += delegate { SaveStreamOptions(); };

            var pingBtn = MerlinUi.MakeButton("Ping");
            pingBtn.Click += delegate { RunPing(); };

            var testLiveBtn = MerlinUi.MakeButton("Test live POST");
            testLiveBtn.Click += delegate { TestLivePost(); };

            var uploadBtn = MerlinUi.MakeButton("Flush log now");
            uploadBtn.Click += delegate { FlushLogNow(); };

            var clearBtn = MerlinUi.MakeButton("Clear trace");
            clearBtn.Click += delegate { ClearTrace(); };

            _previewTimer = new System.Windows.Forms.Timer();
            _previewTimer.Interval = 1500;
            _previewTimer.Tick += delegate { RefreshTracePreview(); };

            Controls.Add(_status);
            Controls.Add(clearBtn);
            Controls.Add(uploadBtn);
            Controls.Add(testLiveBtn);
            Controls.Add(pingBtn);
            Controls.Add(saveBtn);
            Controls.Add(_previewBox);
            Controls.Add(MerlinUi.MakeCaption("Trace preview (also on PC)"));
            Controls.Add(_liveBinBox);
            Controls.Add(MerlinUi.MakeCaption("Live scan bin"));
            Controls.Add(_chkDiagLog);
            Controls.Add(_chkLiveScan);
            Controls.Add(_chkLiveRaw);
            Controls.Add(_urlLabel);
            Controls.Add(MakeUrlHint());

            ApplyTraceEnabled(_cfg.DiagnosticLogFile, false);
        }

        public void OnPanelShown()
        {
            RefreshTracePreview();
            _previewTimer.Enabled = DiagnosticLog.IsEnabled;
        }

        public void OnPanelHidden()
        {
            _previewTimer.Enabled = false;
        }

        public void RefreshTracePreview()
        {
            if (!DiagnosticLog.IsEnabled)
            {
                _previewBox.Text = "(trace off — check box above)";
                return;
            }
            string tail = DiagnosticLog.GetRecentTail(1800);
            string err = DiagnosticLog.LastUploadError;
            string head = DiagnosticLog.LineCount + " lines";
            if (err.Length > 0) head += " UPLOAD:" + MerlinUi.ShortLine(err, 40);
            _previewBox.Text = head + "\r\n" + (tail.Length > 0 ? tail : "(waiting for scans…)");
        }

        private void DiagLog_CheckedChanged(object sender, EventArgs e)
        {
            ApplyTraceEnabled(_chkDiagLog.Checked, true);
        }

        private void ApplyTraceEnabled(bool on, bool saveCfg)
        {
            _cfg.DiagnosticLogFile = on;
            DiagnosticLog.Configure(on);
            _previewTimer.Enabled = on;
            if (on)
            {
                DiagnosticLog.LogSessionStart(_cfg);
                DiagnosticLog.Info("trace enabled — scan on Recv/Find/Diag");
                ThreadPool.QueueUserWorkItem(delegate
                {
                    _api.RegisterScannerSession();
                    DiagnosticLog.FlushToServerNow();
                });
            }
            if (saveCfg)
            {
                try { _cfg.Save(); } catch { }
            }
            _status.Text = on
                ? "Trace ON — open scanner-live on PC"
                : "Trace OFF";
            RefreshTracePreview();
        }

        private Label MakeUrlHint()
        {
            string url = BuildLiveUrl();
            var lbl = new Label
            {
                Height = 32,
                Dock = DockStyle.Top,
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = MerlinUi.FontSm,
                Text = MerlinUi.ShortLine(url, 70),
            };
            return lbl;
        }

        private string BuildLiveUrl()
        {
            return HttpHelper.NormalizeBaseUrl(_cfg.ServerUrl)
                + "/deploy/scanner-live.html?scanner_id="
                + UrlCodec.Encode(_cfg.ScannerId);
        }

        private static CheckBox MakeCheck(string text, bool on)
        {
            return new CheckBox
            {
                Text = text,
                Checked = on,
                Height = 20,
                Dock = DockStyle.Top,
                ForeColor = Color.White,
                Font = MerlinUi.FontSm,
            };
        }

        public void ShowLastRead(string wedgeText)
        {
            if (wedgeText == null) wedgeText = "";
            if (DiagnosticLog.IsEnabled)
            {
                RefreshTracePreview();
                return;
            }
            _previewBox.Text = MerlinUi.ShortLine(wedgeText, 120);
        }

        private void SaveStreamOptions()
        {
            _cfg.LiveRawStream = _chkLiveRaw.Checked;
            _cfg.LiveScanStream = _chkLiveScan.Checked;
            _cfg.DiagnosticLogFile = _chkDiagLog.Checked;
            _cfg.LiveScanBinId = _liveBinBox.Text.Trim();
            ApplyTraceEnabled(_cfg.DiagnosticLogFile, true);
            _status.Text = "Saving + register…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult hb = _api.RegisterScannerSession();
                if (_cfg.LiveRawStream)
                {
                    _api.PostScannerLive("session", "Stream opts saved", null, null, false, "Diag");
                }
                DiagnosticLog.FlushToServerNow();
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = hb.Ok ? "Saved — live ON" : MerlinUi.ShortLine("Save err: " + hb.Error, 80);
                    RefreshTracePreview();
                }), null, EventArgs.Empty);
            });
        }

        private void RunPing()
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

        private void TestLivePost()
        {
            SaveStreamOptions();
            _status.Text = "Live POST…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.PostScannerLive(
                    "test",
                    "diag-ping " + DateTime.UtcNow.Ticks,
                    null,
                    null,
                    false,
                    "Diag");
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = res.Ok
                        ? "Live OK — open browser URL"
                        : MerlinUi.ShortLine(res.Error, 80);
                }), null, EventArgs.Empty);
            });
        }

        private void FlushLogNow()
        {
            _status.Text = "Flushing…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                DiagnosticLog.FlushToServerNow();
                string text = DiagnosticLog.ExportForUpload();
                HttpResult res = _api.UploadDiagnosticLog(text, false);
                BeginInvoke(new EventHandler(delegate
                {
                    RefreshTracePreview();
                    _status.Text = res.Ok
                        ? "Flushed " + text.Length + " B"
                        : MerlinUi.ShortLine(res.Error, 80);
                }), null, EventArgs.Empty);
            });
        }

        private void ClearTrace()
        {
            DiagnosticLog.Clear();
            ThreadPool.QueueUserWorkItem(delegate
            {
                _api.UploadDiagnosticLog("", true);
            });
            _status.Text = "Trace cleared";
            RefreshTracePreview();
        }
    }
}
