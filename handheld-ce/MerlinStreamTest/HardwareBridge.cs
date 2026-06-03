using System;
using System.Windows.Forms;

namespace MerlinStream
{
    public sealed class HardwareBridge : IDisposable
    {
        private readonly Form _form;
        private readonly WedgeInputCapture _wedge;
        private readonly NurApiBridge _nur;
        private string _inputMode = "rfid";
        private bool _typingMode;

        public HardwareBridge(Form form, StreamConfig cfg)
        {
            _form = form;
            _wedge = new WedgeInputCapture();
            _nur = new NurApiBridge(cfg);
            _form.Controls.Add(_wedge);
            _wedge.BringToFront();
            _wedge.LineReceived += WedgeOnLineReceived;
            _nur.TagsInventoryReady += NurOnTagsReady;
            _form.Activated += Form_Activated;
            ArmWedgeCapture();
            WireTypingFields(_form);
            _nur.Start();
        }

        public bool NurAvailable
        {
            get { return _nur.IsAvailable; }
        }

        public void SetInputMode(string mode)
        {
            _inputMode = mode == "barcode" ? "barcode" : "rfid";
        }

        public void SetTypingMode(bool typing)
        {
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new EventHandler(delegate { SetTypingMode(typing); }), null, EventArgs.Empty);
                return;
            }
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
            if (_typingMode) return;
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

        private void Form_Activated(object sender, EventArgs e)
        {
            ArmWedgeCapture();
        }

        private void WedgeOnLineReceived(object sender, WedgeLineEventArgs e)
        {
            RouteInput(e.Line, "wedge", true);
        }

        private void NurOnTagsReady(object sender, NurTagsEventArgs e)
        {
            RouteInput(e.WedgeText, "nur", e.IsComplete);
        }

        private void RouteInput(string text, string source, bool isCompleteScan)
        {
            if (text == null || text.Length == 0) return;
            if (_inputMode != "rfid") return;
            if (RfidDataReceived != null)
            {
                RfidDataReceived(this, new HardwareRfidEventArgs(text, source, isCompleteScan));
            }
            ArmWedgeCapture();
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
}
