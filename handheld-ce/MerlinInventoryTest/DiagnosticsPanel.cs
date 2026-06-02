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
            _previewBox.Height = 40;
            _previewBox.ReadOnly = true;

            _chkLiveRaw = MakeCheck("Live raw → server", _cfg.LiveRawStream);
            _chkLiveScan = MakeCheck("Live scan → inventory", _cfg.LiveScanStream);
            _chkDiagLog = MakeCheck("Log to file on gun", _cfg.DiagnosticLogFile);

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

            var uploadBtn = MerlinUi.MakeButton("Upload log");
            uploadBtn.Click += delegate { UploadLog(); };

            var clearBtn = MerlinUi.MakeButton("Clear file log");
            clearBtn.Click += delegate
            {
                DiagnosticLog.Clear();
                _status.Text = "File log cleared";
            };

            Controls.Add(_status);
            Controls.Add(clearBtn);
            Controls.Add(uploadBtn);
            Controls.Add(testLiveBtn);
            Controls.Add(pingBtn);
            Controls.Add(saveBtn);
            Controls.Add(_previewBox);
            Controls.Add(MerlinUi.MakeCaption("Last read"));
            Controls.Add(_liveBinBox);
            Controls.Add(MerlinUi.MakeCaption("Live scan bin"));
            Controls.Add(_chkDiagLog);
            Controls.Add(_chkLiveScan);
            Controls.Add(_chkLiveRaw);
            Controls.Add(_urlLabel);
            Controls.Add(MakeUrlHint());
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
            _previewBox.Text = MerlinUi.ShortLine(wedgeText, 120);
        }

        private void SaveStreamOptions()
        {
            _cfg.LiveRawStream = _chkLiveRaw.Checked;
            _cfg.LiveScanStream = _chkLiveScan.Checked;
            _cfg.DiagnosticLogFile = _chkDiagLog.Checked;
            _cfg.LiveScanBinId = _liveBinBox.Text.Trim();
            _cfg.Save();
            if (_cfg.DiagnosticLogFile)
            {
                DiagnosticLog.Info("Stream opts saved live_raw=" + _cfg.LiveRawStream + " live_scan=" + _cfg.LiveScanStream);
            }
            _status.Text = "Saving + register…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult hb = _api.RegisterScannerSession();
                if (_cfg.LiveRawStream)
                {
                    _api.PostScannerLive("session", "Stream opts saved", null, null, false, "Diag");
                }
                BeginInvoke(new EventHandler(delegate
                {
                    _status.Text = hb.Ok ? "Saved — live ON" : MerlinUi.ShortLine("Save err: " + hb.Error, 80);
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

        private void UploadLog()
        {
            _status.Text = "Uploading…";
            string text = DiagnosticLog.ReadAll();
            if (text.Length == 0)
            {
                text = "(empty log " + DateTime.UtcNow.ToString("o") + ")";
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _api.UploadDiagnosticLog(text, false);
                BeginInvoke(new EventHandler(delegate
                {
                    if (res.Ok)
                    {
                        _status.Text = "Log on server — download in browser";
                    }
                    else
                    {
                        _status.Text = MerlinUi.ShortLine(res.Error, 80);
                    }
                }), null, EventArgs.Empty);
            });
        }
    }
}
