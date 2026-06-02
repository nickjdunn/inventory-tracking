using System.Drawing;
using System.Windows.Forms;

namespace MerlinHandheld
{
    /// <summary>Layout constants for Nordic Merlin QVGA (240 x 320).</summary>
    internal static class MerlinUi
    {
        public const int ScreenW = 240;
        public const int ScreenH = 320;
        public const int StatusH = 18;
        public const int NavH = 38;
        public const int Margin = 4;
        public const int BtnH = 26;
        public const int FieldH = 20;

        public static readonly Color Bg = Color.FromArgb(15, 23, 42);
        public static readonly Color Card = Color.FromArgb(30, 41, 59);
        public static readonly Color Accent = Color.FromArgb(56, 189, 248);
        public static readonly Color BtnPrimary = Color.FromArgb(3, 105, 161);

        public static int ContentW
        {
            get { return ScreenW - Margin * 2; }
        }

        public static Font FontSm
        {
            get { return new Font("Tahoma", 7f, FontStyle.Regular); }
        }

        public static Font FontSmBold
        {
            get { return new Font("Tahoma", 7f, FontStyle.Bold); }
        }

        public static void StyleForm(Form form)
        {
            form.Width = ScreenW;
            form.Height = ScreenH;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.BackColor = Bg;
            form.Font = FontSm;
        }

        public static void StylePanel(Control c)
        {
            c.BackColor = Bg;
            c.ForeColor = Color.White;
            c.Font = FontSm;
        }

        public static Label MakeCaption(string text)
        {
            return new Label
            {
                Text = text,
                Height = 14,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.TopLeft,
                ForeColor = Color.FromArgb(148, 163, 184),
            };
        }

        public static Button MakePrimaryButton(string text)
        {
            return new Button
            {
                Text = text,
                Height = BtnH,
                Dock = DockStyle.Top,
                BackColor = BtnPrimary,
                ForeColor = Color.White,
                Font = FontSmBold,
            };
        }

        public static Button MakeButton(string text)
        {
            return new Button
            {
                Text = text,
                Height = BtnH,
                Dock = DockStyle.Top,
                BackColor = Card,
                ForeColor = Color.White,
                Font = FontSm,
            };
        }

        public static TextBox MakeField()
        {
            return new TextBox
            {
                Height = FieldH,
                Dock = DockStyle.Top,
                BackColor = Card,
                ForeColor = Color.White,
                Font = FontSm,
            };
        }

        public static ComboBox MakeCombo()
        {
            return new ComboBox
            {
                Height = FieldH + 4,
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Card,
                ForeColor = Color.White,
                Font = FontSm,
            };
        }

        public static Label MakeStatusLabel()
        {
            return new Label
            {
                Height = 36,
                Dock = DockStyle.Bottom,
                TextAlign = ContentAlignment.TopLeft,
                ForeColor = Color.FromArgb(203, 213, 225),
            };
        }

        public static string ShortLine(string text, int maxChars)
        {
            if (text == null) return "";
            text = text.Trim();
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars - 1) + "~";
        }
    }
}
