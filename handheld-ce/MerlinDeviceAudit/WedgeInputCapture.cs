using System;
using System.Text;
using System.Windows.Forms;

namespace MerlinAudit
{
    public sealed class WedgeInputCapture : TextBox
    {
        private const int IdleMs = 420;
        private const int MinAutoChars = 4;

        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly System.Windows.Forms.Timer _idleTimer;

        public event EventHandler<WedgeLineEventArgs> LineReceived;
        public event EventHandler Activity;

        public int BufferLength
        {
            get { return _buffer.Length; }
        }

        public WedgeInputCapture()
        {
            Width = 1;
            Height = 1;
            TabStop = false;
            ReadOnly = false;
            BorderStyle = BorderStyle.None;
            Font = new System.Drawing.Font("Tahoma", 1f, System.Drawing.FontStyle.Regular);
            BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
            ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);

            _idleTimer = new System.Windows.Forms.Timer();
            _idleTimer.Interval = IdleMs;
            _idleTimer.Tick += IdleTimer_Tick;
        }

        public void ArmCapture()
        {
            _idleTimer.Enabled = false;
            _buffer.Length = 0;
            Text = "";
            Focus();
            SelectAll();
        }

        public void FeedKeyPress(KeyPressEventArgs e)
        {
            OnKeyPress(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            string t = Text;
            if (t == null || t.Length == 0) return;

            int term = IndexOfTerminator(t);
            if (term >= 0)
            {
                string line = term > 0 ? t.Substring(0, term) : _buffer.ToString();
                if (line.Length == 0) line = _buffer.ToString();
                CompleteLine(line);
                return;
            }

            if (t.Length >= MinAutoChars)
            {
                _buffer.Length = 0;
                _buffer.Append(t);
                ScheduleIdleComplete();
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r' || e.KeyChar == '\n' || e.KeyChar == '\t')
            {
                string line = _buffer.ToString();
                _buffer.Length = 0;
                Text = "";
                _idleTimer.Enabled = false;
                e.Handled = true;
                CompleteLine(line);
                return;
            }

            if (e.KeyChar >= 32 && _buffer.Length < ScanLimits.MaxWedgeBufferChars)
            {
                _buffer.Append(e.KeyChar);
                if (Activity != null) Activity(this, EventArgs.Empty);
                ScheduleIdleComplete();
            }
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
            {
                string line = _buffer.ToString();
                if (line.Length == 0 && Text != null) line = Text.Trim();
                _buffer.Length = 0;
                Text = "";
                _idleTimer.Enabled = false;
                e.Handled = true;
                CompleteLine(line);
                return;
            }
            base.OnKeyDown(e);
        }

        private void IdleTimer_Tick(object sender, EventArgs e)
        {
            _idleTimer.Enabled = false;
            if (_buffer.Length >= MinAutoChars)
            {
                CompleteLine(_buffer.ToString());
                return;
            }
            if (Text != null && Text.Trim().Length >= MinAutoChars)
            {
                CompleteLine(Text.Trim());
            }
        }

        private void ScheduleIdleComplete()
        {
            if (_buffer.Length < MinAutoChars) return;
            _idleTimer.Enabled = false;
            _idleTimer.Enabled = true;
        }

        private static int IndexOfTerminator(string t)
        {
            int cr = t.IndexOf('\r');
            int lf = t.IndexOf('\n');
            if (cr >= 0) return cr;
            if (lf >= 0) return lf;
            return t.IndexOf('\t');
        }

        private void CompleteLine(string line)
        {
            _idleTimer.Enabled = false;
            _buffer.Length = 0;
            Text = "";
            line = line == null ? "" : line.Trim();
            if (line.Length > 0 && LineReceived != null)
            {
                LineReceived(this, new WedgeLineEventArgs(line));
            }
        }
    }
}
