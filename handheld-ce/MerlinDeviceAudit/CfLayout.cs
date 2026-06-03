using System.Windows.Forms;

namespace MerlinAudit
{
    internal static class CfLayout
    {
        public static void Place(Control c, int x, int y, int w, int h)
        {
            if (c == null) return;
            c.Left = x;
            c.Top = y;
            c.Width = w;
            c.Height = h;
        }
    }
}
