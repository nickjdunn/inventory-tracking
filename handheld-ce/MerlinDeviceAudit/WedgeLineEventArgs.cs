using System;
using System.Windows.Forms;

namespace MerlinAudit
{
    public sealed class WedgeLineEventArgs : EventArgs
    {
        public readonly string Line;
        public WedgeLineEventArgs(string line) { Line = line ?? ""; }
    }
}
