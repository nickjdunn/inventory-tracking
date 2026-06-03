using System;
using System.Collections;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MerlinAudit
{
    public class MainForm : Form
    {
        private readonly AuditConfig _cfg = AuditConfig.Load();
        private readonly AuditClient _client;
        private readonly TextBox _serverBox;
        private readonly TextBox _scannerBox;
        private readonly TextBox _summaryBox;
        private readonly Label _status;
        private string _lastReportJson = "";
        private string _lastScanSessionJson = "";

        public MainForm()
        {
            _client = new AuditClient(_cfg);
            Text = "Merlin Audit " + AuditConfig.AppVersion;
            Width = 240;
            Height = 320;
            BackColor = Color.FromArgb(15, 23, 42);
            ForeColor = Color.White;
            Font = new Font("Tahoma", 8f, FontStyle.Regular);

            _status = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                ForeColor = Color.White,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                Text = "Device audit tool",
            };

            _summaryBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Text = "1) Scan guide — test trigger + barcode\r\n2) Full audit — apps + upload\r\nPC: /deploy/device-audit.html",
            };

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 104, BackColor = Color.FromArgb(30, 41, 59) };

            var guideBtn = MakeButton("Scan guide", 0, true);
            guideBtn.Click += delegate { RunScanGuide(); };

            var fullBtn = MakeButton("Full audit + upload", 26, true);
            fullBtn.Click += delegate { RunFullAudit(true); };

            var runLocalBtn = MakeButton("Inventory only", 52);
            runLocalBtn.Click += delegate { RunFullAudit(false); };

            var uploadBtn = MakeButton("Re-upload last", 78);
            uploadBtn.Click += delegate { ReuploadLast(); };

            btnPanel.Controls.Add(uploadBtn);
            btnPanel.Controls.Add(runLocalBtn);
            btnPanel.Controls.Add(fullBtn);
            btnPanel.Controls.Add(guideBtn);

            var settings = new Panel { Dock = DockStyle.Bottom, Height = 72 };
            _serverBox = MakeField(_cfg.ServerUrl);
            _scannerBox = MakeField(_cfg.ScannerId);
            var pingBtn = new Button { Text = "Ping", Width = 48, Height = 22, Left = 0, Top = 50 };
            pingBtn.Click += delegate { RunPing(); };
            var saveBtn = new Button { Text = "Save", Width = 48, Height = 22, Left = 54, Top = 50 };
            saveBtn.Click += delegate { SaveSettings(); };
            settings.Controls.Add(saveBtn);
            settings.Controls.Add(pingBtn);
            settings.Controls.Add(_scannerBox);
            settings.Controls.Add(Lbl("Scanner ID"));
            settings.Controls.Add(_serverBox);
            settings.Controls.Add(Lbl("Server"));

            Controls.Add(_summaryBox);
            Controls.Add(btnPanel);
            Controls.Add(settings);
            Controls.Add(_status);

            Load += delegate { RunPing(); };
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

        private Button MakeButton(string text, int top, bool primary)
        {
            return new Button
            {
                Text = text,
                Left = 4,
                Top = top,
                Width = 228,
                Height = 22,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = primary ? Color.FromArgb(3, 105, 161) : Color.FromArgb(51, 65, 85),
            };
        }

        private Button MakeButton(string text, int top)
        {
            return MakeButton(text, top, false);
        }

        private void SaveSettings()
        {
            _cfg.ServerUrl = HttpHelper.NormalizeBaseUrl(_serverBox.Text);
            _cfg.ScannerId = _scannerBox.Text.Trim();
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
            var guide = new GuidedScanForm();
            guide.ShowDialog();
            _lastScanSessionJson = guide.SessionJson;
            _summaryBox.Text = BuildScanSummary(_lastScanSessionJson);
            _status.Text = guide.Completed ? "Guide done" : "Guide partial";
        }

        private void RunFullAudit(bool upload)
        {
            SaveSettings();
            _status.Text = "Scan guide…";
            var guide = new GuidedScanForm();
            if (guide.ShowDialog() == DialogResult.OK)
            {
                _lastScanSessionJson = guide.SessionJson;
            }
            else
            {
                _lastScanSessionJson = guide.SessionJson;
            }

            _status.Text = "Collecting…";
            _summaryBox.Text = "Walking storage…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string reportJson = "";
                string summary = "";
                string err = "";
                try
                {
                    var collector = new DeviceAuditCollector(_cfg);
                    reportJson = collector.CollectReportJson(_lastScanSessionJson);
                    summary = BuildSummary(reportJson);
                }
                catch (Exception ex)
                {
                    err = ex.Message ?? "collect failed";
                }

                HttpResult uploadRes = null;
                if (err.Length == 0 && upload)
                {
                    uploadRes = _client.UploadReport(reportJson);
                    if (!uploadRes.Ok)
                    {
                        err = uploadRes.Error ?? "upload failed";
                    }
                }

                BeginInvoke(new EventHandler(delegate
                {
                    if (err.Length > 0)
                    {
                        _status.Text = Short(err, 60);
                        _summaryBox.Text = summary.Length > 0
                            ? summary + "\r\n\r\nERROR: " + err
                            : ("ERROR: " + err);
                        return;
                    }

                    _lastReportJson = reportJson;
                    _summaryBox.Text = summary;
                    if (upload && uploadRes != null)
                    {
                        string id = SimpleJson.ExtractString(uploadRes.Body, "id");
                        _status.Text = id.Length > 0 ? ("Uploaded " + id) : "Uploaded OK";
                        _summaryBox.Text = summary + "\r\n\r\nPC: /deploy/device-audit.html";
                    }
                    else
                    {
                        _status.Text = "Audit done (local)";
                    }
                }), null, EventArgs.Empty);
            });
        }

        private void ReuploadLast()
        {
            if (_lastReportJson == null || _lastReportJson.Length == 0)
            {
                _status.Text = "Run audit first";
                return;
            }
            SaveSettings();
            _status.Text = "Uploading…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                HttpResult res = _client.UploadReport(_lastReportJson);
                BeginInvoke(new EventHandler(delegate
                {
                    if (!res.Ok)
                    {
                        _status.Text = Short(res.Error, 60);
                        return;
                    }
                    string id = SimpleJson.ExtractString(res.Body, "id");
                    _status.Text = id.Length > 0 ? ("Uploaded " + id) : "Uploaded OK";
                }), null, EventArgs.Empty);
            });
        }

        private static string BuildScanSummary(string sessionJson)
        {
            if (sessionJson == null || sessionJson.Length == 0) return "(no scan data)";
            bool completed = SimpleJson.ExtractBool(sessionJson, "completed", false);
            int captures = CountObjectsInArray(sessionJson, "events");
            var sb = new System.Text.StringBuilder();
            sb.Append("Scan guide: ").Append(completed ? "complete" : "partial").Append("\r\n");
            sb.Append("Captures: ").Append(captures).Append(" / 7\r\n\r\n");
            AppendScanEvents(sessionJson, sb);
            return sb.ToString();
        }

        private static void AppendScanEvents(string json, System.Text.StringBuilder sb)
        {
            int idx = json.IndexOf("\"events\"");
            if (idx < 0) return;
            string tail = json.Substring(idx);
            int start = 0;
            while (true)
            {
                int stepKey = tail.IndexOf("\"step_label\":\"", start);
                if (stepKey < 0) break;
                int labelStart = stepKey + 14;
                int labelEnd = tail.IndexOf('"', labelStart);
                if (labelEnd < 0) break;
                string label = tail.Substring(labelStart, labelEnd - labelStart);

                int typeKey = tail.IndexOf("\"classified_type\":\"", labelEnd);
                string ctype = "";
                if (typeKey >= 0 && typeKey < labelEnd + 120)
                {
                    int ts = typeKey + 19;
                    int te = tail.IndexOf('"', ts);
                    if (te > ts) ctype = tail.Substring(ts, te - ts);
                }

                int matchKey = tail.IndexOf("\"matches_expected\":", labelEnd);
                bool match = false;
                if (matchKey >= 0 && matchKey < labelEnd + 120)
                {
                    match = tail.IndexOf("true", matchKey) == matchKey + 19;
                }

                sb.Append(match ? "OK " : "?? ");
                sb.Append(ctype).Append(" — ").Append(label).Append("\r\n");
                start = labelEnd + 1;
            }
        }

        private static string BuildSummary(string reportJson)
        {
            if (reportJson == null || reportJson.Length == 0) return "(empty report)";

            string scanPart = BuildScanSummary(ExtractScanSession(reportJson));

            bool pingOk = false;
            int networkIdx = reportJson.IndexOf("\"network\"");
            if (networkIdx >= 0)
            {
                pingOk = SimpleJson.ExtractBool(reportJson.Substring(networkIdx), "ping_ok", false);
            }
            int knownCount = CountObjectsInArray(reportJson, "known_apps");
            int fileCount = CountObjectsInArray(reportJson, "installed_files");
            string machine = "";
            string os = "";
            int systemIdx = reportJson.IndexOf("\"system\"");
            if (systemIdx >= 0)
            {
                string sys = reportJson.Substring(systemIdx);
                machine = SimpleJson.ExtractString(sys, "machine_name");
                os = SimpleJson.ExtractString(sys, "os_version");
            }
            var sb = new System.Text.StringBuilder();
            sb.Append(scanPart).Append("\r\n");
            sb.Append("Machine: ").Append(machine).Append("\r\n");
            sb.Append("OS: ").Append(Short(os, 50)).Append("\r\n");
            sb.Append("Files indexed: ").Append(fileCount).Append("\r\n");
            sb.Append("Known apps: ").Append(knownCount).Append("\r\n");
            sb.Append("Server ping: ").Append(pingOk ? "OK" : "FAIL").Append("\r\n\r\n");
            AppendKnownAppNames(reportJson, sb);
            return sb.ToString();
        }

        private static string ExtractScanSession(string reportJson)
        {
            int idx = reportJson.IndexOf("\"scan_session\"");
            if (idx < 0) return "";
            int brace = reportJson.IndexOf('{', idx);
            if (brace < 0) return "";
            int depth = 0;
            for (int i = brace; i < reportJson.Length; i++)
            {
                if (reportJson[i] == '{') depth++;
                else if (reportJson[i] == '}')
                {
                    depth--;
                    if (depth == 0) return reportJson.Substring(brace, i - brace + 1);
                }
            }
            return "";
        }

        private static void AppendKnownAppNames(string json, System.Text.StringBuilder sb)
        {
            int idx = json.IndexOf("\"known_apps\"");
            if (idx < 0) return;
            string tail = json.Substring(idx);
            int start = 0;
            sb.Append("Nordic / Merlin:\r\n");
            while (true)
            {
                int nameKey = tail.IndexOf("\"name\":\"", start);
                if (nameKey < 0) break;
                int valStart = nameKey + 8;
                int valEnd = tail.IndexOf('"', valStart);
                if (valEnd < 0) break;
                sb.Append("- ").Append(tail.Substring(valStart, valEnd - valStart)).Append("\r\n");
                start = valEnd + 1;
                if (start > tail.Length - 10) break;
            }
        }

        private static int CountObjectsInArray(string json, string arrayKey)
        {
            int idx = json.IndexOf("\"" + arrayKey + "\"");
            if (idx < 0) return 0;
            int startBracket = json.IndexOf('[', idx);
            int endBracket = json.IndexOf(']', startBracket);
            if (startBracket < 0 || endBracket < 0) return 0;
            string section = json.Substring(startBracket, endBracket - startBracket);
            int count = 0;
            for (int i = 0; i < section.Length; i++)
            {
                if (section[i] == '{') count++;
            }
            return count;
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
