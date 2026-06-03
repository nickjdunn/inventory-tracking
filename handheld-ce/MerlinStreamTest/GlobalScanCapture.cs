using System;
using System.Text;
using System.Windows.Forms;

namespace MerlinStream
{
    /// <summary>
    /// Captures keyboard-wedge scans at the application level without requiring text field focus.
    /// </summary>
    public sealed class GlobalScanCapture : IMessageFilter
    {
        private const int WmChar = 0x0102;

        private readonly StringBuilder _buffer = new StringBuilder();
        private bool _installed;
        private bool _armed = true;

        public event EventHandler<WedgeLineEventArgs> LineReceived;

        public bool Armed
        {
            get { return _armed; }
            set
            {
                _armed = value;
                if (!value) ResetBuffer();
            }
        }

        public void Install()
        {
            if (_installed) return;
            Application.AddMessageFilter(this);
            _installed = true;
        }

        public void Uninstall()
        {
            if (!_installed) return;
            Application.RemoveMessageFilter(this);
            _installed = false;
        }

        public void ResetBuffer()
        {
            _buffer.Length = 0;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (!_armed || m.Msg != WmChar) return false;

            char c = (char)(m.WParam.ToInt32() & 0xFFFF);
            if (c == '\r' || c == '\n')
            {
                string line = _buffer.ToString().Trim();
                ResetBuffer();
                if (line.Length > 0 && LineReceived != null)
                {
                    LineReceived(this, new WedgeLineEventArgs(line));
                }
                return true;
            }

            if (c >= 32 && _buffer.Length < ScanLimits.MaxWedgeBufferChars)
            {
                _buffer.Append(c);
            }
            return true;
        }
    }
}
