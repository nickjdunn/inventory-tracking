using System;
using System.Drawing;
using System.Windows.Forms;

namespace MerlinAudit
{
    /// <summary>Ask tag-stack distance, count, and spacing before RF bench.</summary>
    internal sealed class RfBenchStackSetupForm : Form
    {
        private readonly TextBox _distBox;
        private readonly TextBox _countBox;
        private readonly TextBox _spaceBox;
        public RfBenchStackSetup ResultSetup;

        public RfBenchStackSetupForm(RfBenchStackSetup initial)
        {
            UiTheme.ApplyForm(this);
            Text = "Tag stack setup";
            Width = 240;
            Height = 280;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            var hdr = UiTheme.MakeHeader("Stack in front of reader");
            CfLayout.Place(hdr, 6, 4, 228, 28);

            var h1 = UiTheme.MakeHint("Distance to tags (inches, max 2 decimals)");
            CfLayout.Place(h1, 6, 32, 228, 12);
            _distBox = UiTheme.MakeField(initial != null ? initial.DistanceText : "12");
            CfLayout.Place(_distBox, 6, 46, 228, 22);

            var h2 = UiTheme.MakeHint("Approximate number of tags");
            CfLayout.Place(h2, 6, 70, 228, 12);
            _countBox = UiTheme.MakeField(initial != null ? initial.TagCountText : "100");
            CfLayout.Place(_countBox, 6, 84, 228, 22);

            var h3 = UiTheme.MakeHint("Spacing between tags (inches, max 2 decimals)");
            CfLayout.Place(h3, 6, 108, 228, 12);
            _spaceBox = UiTheme.MakeField(initial != null ? initial.SpacingText : "0.02");
            CfLayout.Place(_spaceBox, 6, 122, 228, 22);

            var hint = UiTheme.MakeHint("8=save stack · 0=cancel · bench 1=run");
            CfLayout.Place(hint, 6, 148, 228, 24);

            var ok = UiTheme.MakeHotkeyButton(8, "Save stack settings", true);
            CfLayout.Place(ok, 6, 178, 228, 34);
            ok.Click += delegate { Confirm(); };

            var cancel = UiTheme.MakeHotkeyButton(0, "Cancel", false);
            CfLayout.Place(cancel, 6, 216, 228, 34);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.D8 || e.KeyCode == Keys.NumPad8) { Confirm(); e.Handled = true; }
                if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    e.Handled = true;
                }
                if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) { e.Handled = true; }
            };

            Controls.Add(cancel);
            Controls.Add(ok);
            Controls.Add(hint);
            Controls.Add(_spaceBox);
            Controls.Add(h3);
            Controls.Add(_countBox);
            Controls.Add(h2);
            Controls.Add(_distBox);
            Controls.Add(h1);
            Controls.Add(hdr);
        }

        private void Confirm()
        {
            RfBenchStackSetup setup;
            string err;
            if (!RfBenchStackSetup.TryParse(_distBox.Text, _countBox.Text, _spaceBox.Text, out setup, out err))
            {
                MessageBox.Show(err, "Stack setup", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
                return;
            }
            ResultSetup = setup;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
