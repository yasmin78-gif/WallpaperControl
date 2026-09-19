using System;
using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class CalendarSourceEditorForm : Form
    {
        private readonly CalendarSource original;
        private readonly TextBox nameBox = new() { Dock = DockStyle.Fill };
        internal readonly PrivateCalendarTextBox UrlBox = new() { Dock = DockStyle.Fill };
        private readonly ComboBox typeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly CheckBox enabledBox = new() { AutoSize = true };
        private readonly Panel colorPreview = new() { Size = new Size(52, 28), BorderStyle = BorderStyle.FixedSingle };
        private int color;
        internal CalendarSource Source { get; private set; }

        internal CalendarSourceEditorForm(CalendarSource source, bool dark, string language)
        {
            original = source;
            Source = source;
            color = CalendarSource.ValidateColor(source.ColorArgb);
            string Get(string key) => Localization.Get(key, language);
            Text = Get("CalendarSourceEditorTitle");
            ClientSize = new Size(720, 365);
            MinimumSize = new Size(650, 405);
            FormBorderStyle = FormBorderStyle.Sizable;
            TableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 3, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            void Row(string label, Control control, int row)
            {
                Label caption = new() { Text = Get(label), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 14, 10) };
                layout.Controls.Add(caption, 0, row);
                control.AccessibleName = caption.Text;
                layout.Controls.Add(control, 1, row);
                control.Margin = new Padding(3, 8, 3, 8);
            }
            nameBox.Text = source.Name;
            nameBox.PlaceholderText = source.DisplayName(language);
            Row("CalendarSourceName", nameBox, 0);
            UrlBox.Text = source.Url;
            Row("CalendarSourceAddress", UrlBox, 1);
            Button reveal = CalendarSourceDialogStyle.Button(Get("SettingsCalendarShowSource"));
            reveal.Click += (_, _) =>
            {
                if (UrlBox.SourcesHidden) { UrlBox.RevealSources(); UrlBox.Multiline = false; UrlBox.AcceptsReturn = false; }
                else UrlBox.HideSources();
                reveal.Text = Get(UrlBox.SourcesHidden ? "SettingsCalendarShowSource" : "SettingsCalendarHideSource");
            };
            layout.Controls.Add(reveal, 2, 1);
            typeBox.Items.AddRange(new object[] { Get("CalendarSourceTypeCalendar"), Get("CalendarSourceTypeHoliday") });
            typeBox.SelectedIndex = source.IsHoliday ? 1 : 0;
            Row("CalendarSourceType", typeBox, 2);
            FlowLayoutPanel colorRow = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            Button choose = CalendarSourceDialogStyle.Button(Get("CalendarSourceChooseColor"));
            colorRow.Controls.Add(colorPreview);
            colorRow.Controls.Add(choose);
            choose.Click += (_, _) =>
            {
                using ColorDialog picker = new() { Color = Color.FromArgb(color), FullOpen = true };
                if (picker.ShowDialog(this) == DialogResult.OK) { color = picker.Color.ToArgb(); colorPreview.BackColor = picker.Color; }
            };
            Row("CalendarSourceColor", colorRow, 3);
            enabledBox.Text = Get("CalendarSourceEnabled");
            enabledBox.Checked = source.Enabled;
            layout.Controls.Add(enabledBox, 1, 4);
            Label hint = new() { Text = Get("CalendarSourceNameHint"), AutoSize = true, MaximumSize = new Size(660, 0), Margin = new Padding(3, 10, 3, 10) };
            layout.Controls.Add(hint, 0, 5);
            layout.SetColumnSpan(hint, 3);
            FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            Button save = CalendarSourceDialogStyle.Button(Get("CalendarSourceApply"));
            Button cancel = CalendarSourceDialogStyle.Button(Get("CalendarSourceCancel"));
            save.Click += (_, _) =>
            {
                if (!TryAccept()) { MessageBox.Show(this, Get("CalendarSourceInvalidAddress"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                DialogResult = DialogResult.OK;
            };
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 6);
            layout.SetColumnSpan(actions, 3);
            Controls.Add(layout);
            AcceptButton = save;
            CancelButton = cancel;
            CalendarSourceDialogStyle.Apply(this, dark);
            colorPreview.BackColor = Color.FromArgb(color);
            FormClosing += (_, _) => UrlBox.HideSources();
        }

        internal bool TryAccept()
        {
            string url = UrlBox.Text.Trim();
            if (!CalendarSource.IsValidUrl(url)) return false;
            Source = original with { Name = nameBox.Text.Trim(), Url = url, Type = (CalendarSourceType)typeBox.SelectedIndex,
                ColorArgb = color, Enabled = enabledBox.Checked };
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                nameBox.Dispose();
                UrlBox.Dispose();
                typeBox.Dispose();
                enabledBox.Dispose();
                colorPreview.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
