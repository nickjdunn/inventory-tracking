using System;
using System.Windows.Forms;

namespace MerlinHandheld
{
    /// <summary>
    /// Routes Merlin hardware input: Nordic NUR trigger inventory, keyboard wedge, and F-keys.
    /// </summary>
    public sealed class HardwareBridge : IDisposable
    {
        private readonly Form _form;
        private readonly WedgeInputCapture _wedge;
        private readonly NurApiBridge _nur;
        private string _inputMode = "rfid";

        public HardwareBridge(Form form, AppConfig cfg)
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

        public string StatusLine
        {
            get
            {
                string w = "Wedge: on";
                return _nur.Status + " | " + w;
            }
        }

        public bool NurAvailable
        {
            get { return _nur.IsAvailable; }
        }

        /// <summary>rfid = Receive/Find trigger data; barcode = Scan key / UPC wedge.</summary>
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
        public event EventHandler<HardwareBarcodeEventArgs> BarcodeReceived;

        private void WedgeOnLineReceived(object sender, WedgeLineEventArgs e)
        {
            RouteInput(e.Line);
        }

        private void NurOnTagsReady(object sender, NurTagsEventArgs e)
        {
            RouteInput(e.WedgeText);
        }

        private void RouteInput(string text)
        {
            if (text == null || text.Length == 0) return;
            if (_inputMode == "barcode")
            {
                string upc = ExtractBarcode(text);
                if (BarcodeReceived != null)
                {
                    BarcodeReceived(this, new HardwareBarcodeEventArgs(upc));
                }
                return;
            }
            if (RfidDataReceived != null)
            {
                RfidDataReceived(this, new HardwareRfidEventArgs(text));
            }
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
        public HardwareRfidEventArgs(string t) { WedgeText = t ?? ""; }
    }

    public sealed class HardwareBarcodeEventArgs : EventArgs
    {
        public readonly string Code;
        public HardwareBarcodeEventArgs(string c) { Code = c ?? ""; }
    }
}
