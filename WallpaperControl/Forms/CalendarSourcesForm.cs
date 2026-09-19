using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class CalendarSourcesForm : Form
    {
        private readonly List<CalendarSource> sources;
        private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true,
            FullRowSelect = true, MultiSelect = false, HideSelection = false };
        private readonly ImageList colors = new() { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
        internal IReadOnlyList<CalendarSource> Sources => sources.ToArray();

        internal CalendarSourcesForm(IEnumerable<CalendarSource> initial, bool dark, string language)
        {
            sources = initial.ToList();
            string Get(string key) => Localization.Get(key, language);
            Text = Get("CalendarSourcesTitle");
            ClientSize = new Size(740, 430);
            MinimumSize = new Size(650, 350);
            Padding = new Padding(16);
            list.Columns.Add(Get("CalendarSourceName"), 400);
            list.Columns.Add(Get("CalendarSourceType"), 245);
            list.SmallImageList = colors;
            // Copy swatches into the native image list before disposing the temporary bitmaps.
            _ = colors.Handle;
            list.AccessibleName = Text;
            bool updating = false;
            void RefreshList()
            {
                updating = true;
                list.BeginUpdate();
                list.Items.Clear();
                colors.Images.Clear();
                foreach (CalendarSource source in sources)
                {
                    using Bitmap swatch = new(20, 20);
                    using (Graphics g = Graphics.FromImage(swatch)) g.Clear(Color.FromArgb(CalendarSource.ValidateColor(source.ColorArgb)));
                    colors.Images.Add(swatch);
                    ListViewItem item = new(source.DisplayName(language), colors.Images.Count - 1) { Checked = source.Enabled, Tag = source.Id };
                    item.SubItems.Add(Get(source.IsHoliday ? "CalendarSourceTypeHoliday" : "CalendarSourceTypeCalendar"));
                    list.Items.Add(item);
                }
                list.EndUpdate();
                updating = false;
            }
            list.ItemChecked += (_, e) =>
            {
                if (!updating && e.Item.Tag is Guid id)
                {
                    int index = sources.FindIndex(source => source.Id == id);
                    if (index >= 0) sources[index] = sources[index] with { Enabled = e.Item.Checked };
                }
            };
            void Edit(bool adding)
            {
                int index = list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0];
                if (!adding && index < 0) return;
                CalendarSource source = adding ? new CalendarSource { FallbackNumber = sources.Count + 1,
                    ColorArgb = CalendarSource.DefaultColor(sources.Count) } : sources[index];
                using CalendarSourceEditorForm editor = new(source, dark, language) { Opacity = Opacity };
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                if (adding) sources.Add(editor.Source); else sources[index] = editor.Source;
                RefreshList();
            }
            FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
            Button add = CalendarSourceDialogStyle.Button(Get("CalendarSourceAdd"));
            Button edit = CalendarSourceDialogStyle.Button(Get("CalendarSourceEdit"));
            Button remove = CalendarSourceDialogStyle.Button(Get("CalendarSourceRemove"));
            Button apply = CalendarSourceDialogStyle.Button(Get("CalendarSourceApply"));
            Button cancel = CalendarSourceDialogStyle.Button(Get("CalendarSourceCancel"));
            add.Click += (_, _) => Edit(true);
            edit.Click += (_, _) => Edit(false);
            remove.Click += (_, _) => { if (list.SelectedIndices.Count > 0) { sources.RemoveAt(list.SelectedIndices[0]); RefreshList(); } };
            list.DoubleClick += (_, _) => Edit(false);
            apply.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.AddRange(new Control[] { add, edit, remove, cancel, apply });
            Controls.Add(list);
            Controls.Add(actions);
            AcceptButton = apply;
            CancelButton = cancel;
            CalendarSourceDialogStyle.Apply(this, dark);
            RefreshList();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) list.Dispose();
            base.Dispose(disposing);
            if (disposing) colors.Dispose();
        }
    }
}
