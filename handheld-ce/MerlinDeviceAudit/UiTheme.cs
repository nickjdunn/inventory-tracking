using System.Drawing;
using System.Windows.Forms;

namespace MerlinAudit
{
    internal static class UiTheme
    {
        public static readonly Color Bg = Color.FromArgb(10, 15, 28);
        public static readonly Color Card = Color.FromArgb(22, 32, 52);
        public static readonly Color CardBorder = Color.FromArgb(45, 62, 92);
        public static readonly Color Accent = Color.FromArgb(56, 189, 248);
        public static readonly Color AccentBtn = Color.FromArgb(2, 132, 199);
        public static readonly Color Btn = Color.FromArgb(40, 54, 78);
        public static readonly Color Text = Color.FromArgb(241, 245, 249);
        public static readonly Color Muted = Color.FromArgb(148, 163, 184);
        public static readonly Color Good = Color.FromArgb(74, 222, 128);
        public static readonly Color Warn = Color.FromArgb(251, 191, 36);

        public static void ApplyForm(Form f)
        {
            f.BackColor = Bg;
            f.ForeColor = Text;
            f.Font = new Font("Tahoma", 8f, FontStyle.Regular);
            f.KeyPreview = true;
        }

        public static Label MakeHeader(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Accent,
                Font = new Font("Tahoma", 9f, FontStyle.Bold),
                Height = 18,
            };
        }

        public static Label MakeHint(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Muted,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
                Height = 14,
            };
        }

        public static TextBox MakeLogBox()
        {
            return new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Card,
                ForeColor = Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Tahoma", 7f, FontStyle.Regular),
            };
        }

        public static TextBox MakeField(string text)
        {
            return new TextBox
            {
                Text = text ?? "",
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Tahoma", 8f, FontStyle.Regular),
            };
        }

        public static Button MakeHotkeyButton(int number, string label, bool primary)
        {
            string num = number >= 0 ? (number.ToString() + "  ") : "";
            return new Button
            {
                Text = num + label,
                Height = 34,
                ForeColor = Text,
                BackColor = primary ? AccentBtn : Btn,
                Font = new Font("Tahoma", 8f, FontStyle.Bold),
            };
        }

        public static Button MakeNavButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 36,
                Height = 26,
                ForeColor = Text,
                BackColor = Btn,
                Font = new Font("Tahoma", 9f, FontStyle.Bold),
            };
        }
    }
}
