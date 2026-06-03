using System;
using System.Collections;
using System.Windows.Forms;

namespace MerlinHandheld
{
    /// <summary>
    /// Routes Merlin hardware input: Nordic NUR trigger inventory, keyboard wedge, and F-keys.
    /// Wedge input uses a hidden capture field kept focused so scans work without tapping a text box.
    /// </summary>
    public sealed class HardwareBridge : IDisposable
    {
        private readonly Form _form;
        private readonly AppConfig _cfg;
        private readonly WedgeInputCapture _wedge;
        private readonly NurApiBridge _nur;
        private string _inputMode = "rfid";
        private bool _typingMode;

        public HardwareBridge(Form form, AppConfig cfg)
        {
            _form = form;
            _cfg = cfg;
            _wedge = new WedgeInputCapture();
            _nur = new NurApiBridge(cfg);

            _form.Controls.Add(_wedge);
            _wedge.BringToFront();

            _wedge.LineReceived += WedgeOnLineReceived;
            _nur.TagsInventoryReady += NurOnTagsReady;

            _form.Activated += Form_Activated;

            if (_cfg.HardwareWedgeEnabled)
            {
                ArmWedgeCapture();
            }

            _nur.Start();
            WireTypingFields(_form);
        }

        public string StatusLine
        {
            get
            {
                string w = _cfg.HardwareWedgeEnabled ? "Wedge: on" : "Wedge: off";
                return _nur.Status + " | " + w;
            }
        }

        public bool NurAvailable
        {
            get { return _nur.IsAvailable; }
        }

        /// <summary>rfid, barcode, or auto (Add mode — classify by scan content).</summary>
        public void SetInputMode(string mode)
        {
            if (mode == "barcode") _inputMode = "barcode";
            else if (mode == "auto") _inputMode = "auto";
            else _inputMode = "rfid";
        }

        public void SetTypingMode(bool typing)
        {
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new EventHandler(delegate { SetTypingMode(typing); }), null, EventArgs.Empty);
                return;
            }
            if (!_cfg.HardwareWedgeEnabled) return;
            _typingMode = typing;
            if (!typing) ArmWedgeCapture();
        }

        public void ArmWedgeCapture()
        {
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new EventHandler(delegate { ArmWedgeCapture(); }), null, EventArgs.Empty);
                return;
            }
            if (!_cfg.HardwareWedgeEnabled || _typingMode) return;
            _wedge.ArmCapture();
        }

        public void RefreshTypingFields()
        {
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new EventHandler(delegate { RefreshTypingFields(); }), null, EventArgs.Empty);
                return;
            }
            WireTypingFields(_form);
        }

        public void FireTriggerInventory()
        {
            if (_nur.IsAvailable)
            {
                _nur.TriggerInventory();
                return;
            }
            ArmWedgeCapture();
        }

        public event EventHandler<HardwareRfidEventArgs> RfidDataReceived;
        public event EventHandler<HardwareBarcodeEventArgs> BarcodeReceived;

        private void Form_Activated(object sender, EventArgs e)
        {
            ArmWedgeCapture();
        }

        private void WedgeOnLineReceived(object sender, WedgeLineEventArgs e)
        {
            if (DiagnosticLog.IsEnabled)
            {
                DiagnosticLog.LogInbound("wedge", _inputMode, null, e.Line);
            }
            RouteInput(e.Line, "wedge", true);
        }

        private void NurOnTagsReady(object sender, NurTagsEventArgs e)
        {
            if (DiagnosticLog.IsEnabled)
            {
                DiagnosticLog.LogInbound("nur", _inputMode, null, e.WedgeText);
            }
            RouteInput(e.WedgeText, "nur", e.IsComplete);
        }

        private void RouteInput(string text, string source, bool isCompleteScan)
        {
            if (text == null || text.Length == 0) return;

            string route = _inputMode;
            if (route == "auto")
            {
                route = ClassifyInput(text);
            }

            if (route == "barcode")
            {
                string upc = ExtractBarcode(text);
                if (BarcodeReceived != null)
                {
                    BarcodeReceived(this, new HardwareBarcodeEventArgs(upc));
                }
                if (_cfg.HardwareWedgeEnabled) ArmWedgeCapture();
                return;
            }

            if (RfidDataReceived != null)
            {
                RfidDataReceived(this, new HardwareRfidEventArgs(text, source, isCompleteScan));
            }
            if (_cfg.HardwareWedgeEnabled) ArmWedgeCapture();
        }

        private static string ClassifyInput(string text)
        {
            string trimmed = text == null ? "" : text.Trim();
            if (trimmed.Length == 0) return "rfid";

            if (trimmed.IndexOf(',') >= 0) return "rfid";

            int pipe = trimmed.IndexOf('|');
            if (pipe > 0)
            {
                string head = trimmed.Substring(0, pipe).Trim();
                if (IsMostlyHex(head) && head.Length >= 12) return "rfid";
            }

            if (IsNumericBarcode(trimmed)) return "barcode";

            ArrayList tags = ScanLimits.ParseTags(trimmed);
            if (tags.Count > 0)
            {
                string epc = ((TagRead)tags[0]).Epc;
                if (IsNumericBarcode(epc) && epc.Length <= 14 && !IsMostlyHex(epc))
                {
                    return "barcode";
                }
            }
            return "rfid";
        }

        private static bool IsNumericBarcode(string s)
        {
            if (s == null) return false;
            s = s.Trim();
            if (s.Length < 6 || s.Length > 20) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        private static bool IsMostlyHex(string s)
        {
            if (s == null || s.Length == 0) return false;
            int hex = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = char.ToUpper(s[i]);
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))
                {
                    hex++;
                }
            }
            return hex >= s.Length * 3 / 4;
        }

        private static string ExtractBarcode(string raw)
        {
            string s = raw.Trim();
            if (s.Length == 0) return "";
            int comma = s.IndexOf(',');
            if (comma >= 0) s = s.Substring(0, comma);
            s = s.Trim();
            return s;
        }

        private void WireTypingFields(Control root)
        {
            if (root == null) return;
            if (root is WedgeInputCapture) return;

            if (root is TextBox)
            {
                TextBox tb = (TextBox)root;
                if (!tb.ReadOnly)
                {
                    tb.GotFocus -= TypingField_GotFocus;
                    tb.LostFocus -= TypingField_LostFocus;
                    tb.GotFocus += TypingField_GotFocus;
                    tb.LostFocus += TypingField_LostFocus;
                }
            }

            foreach (Control child in root.Controls)
            {
                WireTypingFields(child);
            }
        }

        private void TypingField_GotFocus(object sender, EventArgs e)
        {
            SetTypingMode(true);
        }

        private void TypingField_LostFocus(object sender, EventArgs e)
        {
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new EventHandler(delegate { TypingField_LostFocus(sender, e); }), null, EventArgs.Empty);
                return;
            }
            if (!IsTypingFieldFocused())
            {
                SetTypingMode(false);
            }
        }

        private bool IsTypingFieldFocused()
        {
            return HasFocusedTypingField(_form);
        }

        private static bool HasFocusedTypingField(Control root)
        {
            if (root == null) return false;
            if (root is WedgeInputCapture) return false;

            TextBox tb = root as TextBox;
            if (tb != null && !tb.ReadOnly && tb.Focused) return true;

            foreach (Control child in root.Controls)
            {
                if (HasFocusedTypingField(child)) return true;
            }
            return false;
        }

        public void Dispose()
        {
            _form.Activated -= Form_Activated;
            _wedge.LineReceived -= WedgeOnLineReceived;
            _nur.TagsInventoryReady -= NurOnTagsReady;
            _nur.Dispose();
        }
    }

    public sealed class HardwareRfidEventArgs : EventArgs
    {
        public readonly string WedgeText;
        public readonly string Source;
        public readonly bool IsCompleteScan;

        public HardwareRfidEventArgs(string t, string source, bool isCompleteScan)
        {
            WedgeText = t ?? "";
            Source = source ?? "";
            IsCompleteScan = isCompleteScan;
        }
    }

    public sealed class HardwareBarcodeEventArgs : EventArgs
    {
        public readonly string Code;
        public HardwareBarcodeEventArgs(string c) { Code = c ?? ""; }
    }
}
