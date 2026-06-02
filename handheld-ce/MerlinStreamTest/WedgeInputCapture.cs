using System;
using System.Text;
using System.Windows.Forms;

namespace MerlinStream
{
    public sealed class WedgeInputCapture : TextBox
    {
        private readonly StringBuilder _buffer = new StringBuilder();

        public WedgeInputCapture()
        {
            Width = 1;
            Height = 1;
            TabStop = true;
            Font = new System.Drawing.Font("Tahoma", 1f, System.Drawing.FontStyle.Regular);
            BorderStyle = BorderStyle.None;
            BackColor = System.Drawing.Color.FromArgb(30, 41, 59);
            ForeColor = System.Drawing.Color.FromArgb(30, 41, 59);
        }

        public event EventHandler<WedgeLineEventArgs> LineReceived;

        public void ArmCapture()
        {
            _buffer.Length = 0;
            Focus();
            SelectAll();
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r' || e.KeyChar == '\n')
            {
                string line = _buffer.ToString().Trim();
                _buffer.Length = 0;
                Text = "";
                e.Handled = true;
                if (line.Length > 0 && LineReceived != null)
                {
                    LineReceived(this, new WedgeLineEventArgs(line));
                }
                return;
            }

            if (e.KeyChar >= 32)
            {
                if (_buffer.Length < ScanLimits.MaxWedgeBufferChars)
                {
                    _buffer.Append(e.KeyChar);
                }
            }

            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }
    }

    public sealed class WedgeLineEventArgs : EventArgs
    {
        public readonly string Line;
        public WedgeLineEventArgs(string line) { Line = line ?? ""; }
    }
}
