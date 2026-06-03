using System.Windows.Forms;

namespace MerlinAudit
{
    internal static class PageLayout
    {
        public static void Apply(MainForm host, int pageIndex, Panel page, Button[] buttons, TextBox logBox)
        {
            if (host == null || page == null) return;
            int w = host.ContentWidth;
            int h = host.ContentHeight;
            if (w < 80) w = 228;
            if (h < 80) h = 180;

            int btnW = w - 12;
            int btnH = 30;
            int gap = 3;

            if (pageIndex == 3 && logBox != null)
            {
                int bottomRow = h - btnH - 6;
                int logH = bottomRow - 22;
                if (logH < 40) logH = 40;
                CfLayout.Place(logBox, 6, 18, btnW, logH);
                if (buttons != null && buttons.Length > 0)
                {
                    CfLayout.Place(buttons[0], 6, bottomRow, 108, btnH);
                    if (buttons.Length > 1) CfLayout.Place(buttons[1], 118, bottomRow, 108, btnH);
                }
                return;
            }

            if (pageIndex == 2 && buttons != null && buttons.Length >= 2)
            {
                int bottomRow = h - btnH - 6;
                CfLayout.Place(buttons[0], 6, bottomRow, 108, btnH);
                CfLayout.Place(buttons[1], 118, bottomRow, 108, btnH);
                return;
            }

            if (buttons == null || buttons.Length == 0) return;
            int count = buttons.Length;
            int total = count * btnH + (count - 1) * gap;
            int startY = 16;
            if (startY + total > h - 4)
            {
                btnH = 26;
                gap = 2;
                total = count * btnH + (count - 1) * gap;
                startY = 12;
            }
            for (int i = 0; i < count; i++)
            {
                CfLayout.Place(buttons[i], 6, startY + i * (btnH + gap), btnW, btnH);
            }
        }
    }
}
