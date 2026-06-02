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

        public HardwareBridge(Form form, StreamConfig cfg)
        {
            _form = form;
            _wedge = new WedgeInputCapture();
            _nur = new NurApiBridge(cfg);
            _form.Controls.Add(_wedge);
            _wedge.BringToFront();
            _wedge.LineReceived += WedgeOnLineReceived;
            _nur.TagsInventoryReady += NurOnTagsReady;
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

        public void ArmWedgeCapture()
        {
            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(new EventHandler(delegate { ArmWedgeCapture(); }), null, EventArgs.Empty);
                return;
            }
            _wedge.ArmCapture();
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

        private void WedgeOnLineReceived(object sender, WedgeLineEventArgs e)
        {
            RouteInput(e.Line, "wedge");
        }

        private void NurOnTagsReady(object sender, NurTagsEventArgs e)
        {
            RouteInput(e.WedgeText, "nur");
        }

        private void RouteInput(string text, string source)
        {
            if (text == null || text.Length == 0) return;
            if (_inputMode != "rfid") return;
            if (RfidDataReceived != null)
            {
                RfidDataReceived(this, new HardwareRfidEventArgs(text, source));
            }
        }

        public void Dispose()
        {
            _wedge.LineReceived -= WedgeOnLineReceived;
            _nur.TagsInventoryReady -= NurOnTagsReady;
            _nur.Dispose();
        }
    }

    public sealed class HardwareRfidEventArgs : EventArgs
    {
        public readonly string WedgeText;
        public readonly string Source;
        public HardwareRfidEventArgs(string t, string source)
        {
            WedgeText = t ?? "";
            Source = source ?? "";
        }
    }
}
