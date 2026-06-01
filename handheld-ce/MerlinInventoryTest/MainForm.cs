using System;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace MerlinInventoryTest
{
    /// <summary>
    /// First native CE smoke test — HTTP to inventory server only (no RFID yet).
    /// </summary>
    public class MainForm : Form
    {
        private readonly TextBox _serverBox;
        private readonly TextBox _logBox;
        private readonly Label _statusLabel;
        private const string ConfigFileName = "server.txt";
        private const string AppVersion = "0.1.0-test";

        public MainForm()
        {
            Text = "Merlin Inventory Test " + AppVersion;
            Width = 320;
            Height = 420;
            MinimizeBox = false;

            var serverLabel = new Label
            {
                Text = "Server (host:port)",
                Left = 8,
                Top = 8,
                Width = 280,
                Height = 16
            };

            _serverBox = new TextBox
            {
                Left = 8,
                Top = 28,
                Width = 296,
                Text = LoadServerUrl()
            };

            var testBtn = new Button
            {
                Text = "Test connection",
                Left = 8,
                Top = 58,
                Width = 140,
                Height = 32
            };
            testBtn.Click += delegate { RunTests(); };

            var saveBtn = new Button
            {
                Text = "Save server",
                Left = 160,
                Top = 58,
                Width = 144,
                Height = 32
            };
            saveBtn.Click += delegate { SaveServerUrl(); };

            _statusLabel = new Label
            {
                Text = "Ready",
                Left = 8,
                Top = 96,
                Width = 296,
                Height = 20,
                ForeColor = System.Drawing.Color.DarkGreen
            };

            _logBox = new TextBox
            {
                Left = 8,
                Top = 120,
                Width = 296,
                Height = 260,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = new System.Drawing.Font("Tahoma", 8f)
            };

            Controls.Add(serverLabel);
            Controls.Add(_serverBox);
            Controls.Add(testBtn);
            Controls.Add(saveBtn);
            Controls.Add(_statusLabel);
            Controls.Add(_logBox);
        }

        private string ConfigPath()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.IsNullOrEmpty(dir)) dir = @"\";
            return Path.Combine(dir, ConfigFileName);
        }

        private string LoadServerUrl()
        {
            try
            {
                string path = ConfigPath();
                if (File.Exists(path))
                {
                    string line = File.ReadAllText(path).Trim();
                    if (line.Length > 0) return line;
                }
            }
            catch { }
            return "http://10.17.17.17:3000";
        }

        private void SaveServerUrl()
        {
            try
            {
                File.WriteAllText(ConfigPath(), NormalizeBaseUrl(_serverBox.Text));
                AppendLog("Saved server URL.");
                _statusLabel.Text = "Saved";
            }
            catch (Exception ex)
            {
                AppendLog("Save failed: " + ex.Message);
                _statusLabel.Text = "Error";
                _statusLabel.ForeColor = System.Drawing.Color.DarkRed;
            }
        }

        private static string NormalizeBaseUrl(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return "http://10.17.17.17:3000";
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                s = "http://" + s;
            }
            if (s.EndsWith("/")) s = s.Substring(0, s.Length - 1);
            return s;
        }

        private void AppendLog(string line)
        {
            _logBox.AppendText(line + "\r\n");
        }

        private void RunTests()
        {
            _logBox.Clear();
            string baseUrl = NormalizeBaseUrl(_serverBox.Text);
            AppendLog("Base: " + baseUrl);
            _statusLabel.Text = "Testing…";
            _statusLabel.ForeColor = System.Drawing.Color.DarkBlue;

            bool ok1 = HttpGet(baseUrl + "/api/deploy/info", "deploy/info");
            bool ok2 = HttpPostJson(
                baseUrl + "/api/scanner/heartbeat",
                "{\"scanner_id\":\"merlin-ce-native-test\",\"mode\":\"native-test\"}",
                "heartbeat");
            bool ok3 = HttpGet(baseUrl + "/api/handheld/sync", "handheld/sync");

            if (ok1 && ok2 && ok3)
            {
                _statusLabel.Text = "All OK";
                _statusLabel.ForeColor = System.Drawing.Color.DarkGreen;
            }
            else
            {
                _statusLabel.Text = "Failed — check Wi‑Fi";
                _statusLabel.ForeColor = System.Drawing.Color.DarkRed;
            }
        }

        private bool HttpGet(string url, string label)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var stream = res.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    AppendLog(label + ": HTTP " + (int)res.StatusCode);
                    if (body.Length > 120) body = body.Substring(0, 120) + "…";
                    AppendLog(body);
                    return (int)res.StatusCode >= 200 && (int)res.StatusCode < 300;
                }
            }
            catch (Exception ex)
            {
                AppendLog(label + " FAIL: " + ex.Message);
                return false;
            }
        }

        private bool HttpPostJson(string url, string json, string label)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.ContentLength = bytes.Length;
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;
                using (var reqStream = req.GetRequestStream())
                {
                    reqStream.Write(bytes, 0, bytes.Length);
                }
                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    AppendLog(label + ": HTTP " + (int)res.StatusCode);
                    return (int)res.StatusCode >= 200 && (int)res.StatusCode < 300;
                }
            }
            catch (Exception ex)
            {
                AppendLog(label + " FAIL: " + ex.Message);
                return false;
            }
        }
    }
}
