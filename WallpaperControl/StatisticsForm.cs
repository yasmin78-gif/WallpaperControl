using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class StatisticsForm : Form
    {
        private const int
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private const int
            SW_SHOWNOACTIVATE = 4;

        private const int
            SW_HIDE = 0;

        private readonly IReadOnlyDictionary<string, int>
            wallpaperViewCounts;

        private readonly IReadOnlyDictionary<string, DateTime>
            wallpaperLastShown;

        private readonly DateTime statisticsStartedAt;

        private readonly Label summaryLabel;
        private readonly ComboBox filterComboBox;
        private readonly ListView statisticsList;
        private readonly Button closeButton;

        private readonly Form wallpaperPreviewForm;
        private readonly PictureBox wallpaperPreviewPictureBox;
        private readonly Label wallpaperPreviewInfoLabel;

        private readonly bool darkMode;

        private string? previewPath;

        private sealed class FilterChoice
        {
            public string Text { get; }
            public bool Top10Only { get; }

            public FilterChoice(
                string text,
                bool top10Only)
            {
                Text = text;
                Top10Only = top10Only;
            }

            public override string ToString() =>
                Text;
        }

        public StatisticsForm(
            bool darkMode,
            int windowOpacityPercent,
            IReadOnlyDictionary<string, int>
                wallpaperViewCounts,
            IReadOnlyDictionary<string, DateTime>
                wallpaperLastShown,
            DateTime statisticsStartedAt)
        {
            this.darkMode = darkMode;
            this.wallpaperViewCounts =
                wallpaperViewCounts;
            this.wallpaperLastShown =
                wallpaperLastShown;
            this.statisticsStartedAt =
                statisticsStartedAt;

            Text =
                Localization.Get("StatisticsTitle");

            Icon = Icon.ExtractAssociatedIcon(
                Application.ExecutablePath);

            FormBorderStyle =
                FormBorderStyle.FixedDialog;

            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            StartPosition =
                FormStartPosition.CenterParent;

            ClientSize =
                new Size(700, 485);

            Font =
                new Font("Segoe UI", 10);

            Opacity =
                Math.Clamp(
                    windowOpacityPercent,
                    80,
                    100) / 100.0;

            Label titleLabel = new Label
            {
                Text =
                    Localization.Get(
                        "StatisticsTitle"),
                Location =
                    new Point(25, 20),
                AutoSize = true,
                Font =
                    new Font(
                        "Segoe UI",
                        14,
                        FontStyle.Bold)
            };

            summaryLabel = new Label
            {
                Location =
                    new Point(25, 56),
                Size =
                    new Size(650, 46)
            };

            Label filterLabel = new Label
            {
                Text =
                    Localization.Get(
                        "StatisticsFilterLabel"),
                Location =
                    new Point(25, 111),
                Size =
                    new Size(110, 25)
            };

            filterComboBox = new ComboBox
            {
                Location =
                    new Point(140, 106),
                Size =
                    new Size(150, 28),
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            filterComboBox.Items.Add(
                new FilterChoice(
                    Localization.Get(
                        "StatisticsFilterTop10"),
                    true));

            filterComboBox.Items.Add(
                new FilterChoice(
                    Localization.Get(
                        "StatisticsFilterAll"),
                    false));

            filterComboBox.SelectedIndex = 0;

            filterComboBox.SelectedIndexChanged +=
                (_, _) => RefreshStatistics();

            statisticsList = new ListView
            {
                Location =
                    new Point(25, 145),
                Size =
                    new Size(650, 275),
                View =
                    View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HideSelection = true,
                HeaderStyle =
                    ColumnHeaderStyle.Nonclickable,
                TabStop = false
            };

            statisticsList.Columns.Add(
                Localization.Get(
                    "StatisticsRank"),
                45,
                HorizontalAlignment.Left);

            statisticsList.Columns.Add(
                Localization.Get(
                    "StatisticsWallpaper"),
                315,
                HorizontalAlignment.Left);

            statisticsList.Columns.Add(
                Localization.Get(
                    "StatisticsViews"),
                80,
                HorizontalAlignment.Right);

            statisticsList.Columns.Add(
                Localization.Get(
                    "StatisticsLastShown"),
                185,
                HorizontalAlignment.Left);

            statisticsList.MouseMove +=
                StatisticsList_MouseMove;

            statisticsList.MouseLeave +=
                (_, _) => HideWallpaperPreview();

            statisticsList.MouseClick +=
                StatisticsList_MouseClick;

            closeButton = new Button
            {
                Text =
                    Localization.Get(
                        "AboutClose"),
                Location =
                    new Point(550, 435),
                Size =
                    new Size(125, 34),
                DialogResult =
                    DialogResult.OK
            };

            wallpaperPreviewForm = new PreviewForm
            {
                FormBorderStyle =
                    FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition =
                    FormStartPosition.Manual,
                TopMost = true,
                ClientSize =
                    new Size(420, 300),
                Padding =
                    new Padding(8)
            };

            wallpaperPreviewPictureBox =
                new PictureBox
                {
                    Location =
                        new Point(8, 8),
                    Size =
                        new Size(404, 228),
                    SizeMode =
                        PictureBoxSizeMode.Zoom,
                    BackColor =
                        Color.Black
                };

            wallpaperPreviewInfoLabel =
                new Label
                {
                    Location =
                        new Point(8, 244),
                    Size =
                        new Size(404, 48),
                    AutoEllipsis = true
                };

            wallpaperPreviewForm.Controls.Add(
                wallpaperPreviewPictureBox);

            wallpaperPreviewForm.Controls.Add(
                wallpaperPreviewInfoLabel);

            Controls.Add(titleLabel);
            Controls.Add(summaryLabel);
            Controls.Add(filterLabel);
            Controls.Add(filterComboBox);
            Controls.Add(statisticsList);
            Controls.Add(closeButton);

            AcceptButton = closeButton;
            CancelButton = closeButton;

            ApplyTheme();
            RefreshStatistics();
        }

        protected override void OnHandleCreated(
            EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBarTheme();
        }

        protected override void OnFormClosed(
            FormClosedEventArgs e)
        {
            HideWallpaperPreview();

            Image? oldImage =
                wallpaperPreviewPictureBox.Image;

            wallpaperPreviewPictureBox.Image =
                null;

            oldImage?.Dispose();

            wallpaperPreviewForm.Dispose();

            base.OnFormClosed(e);
        }

        private void RefreshStatistics()
        {
            int totalViews =
                wallpaperViewCounts.Values.Sum();

            summaryLabel.Text =
                string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsSummary"),
                    statisticsStartedAt,
                    totalViews,
                    wallpaperViewCounts.Count);

            IEnumerable<KeyValuePair<string, int>>
                items =
                    wallpaperViewCounts
                        .OrderByDescending(
                            item => item.Value)
                        .ThenBy(
                            item =>
                                Path.GetFileName(
                                    item.Key),
                            StringComparer
                                .CurrentCultureIgnoreCase);

            bool top10Only =
                filterComboBox.SelectedItem
                    is FilterChoice choice &&
                choice.Top10Only;

            if (top10Only)
            {
                items = items.Take(10);
            }

            statisticsList.BeginUpdate();

            try
            {
                statisticsList.Items.Clear();

                int rank = 1;

                foreach (KeyValuePair<string, int>
                    item in items)
                {
                    wallpaperLastShown.TryGetValue(
                        item.Key,
                        out DateTime lastShown);

                    string lastShownText =
                        lastShown == default
                        ? ""
                        : lastShown.ToString(
                            "g",
                            Localization.CurrentCulture);

                    ListViewItem row =
                        new ListViewItem(
                            rank.ToString(
                                Localization.CurrentCulture));

                    row.SubItems.Add(
                        Path.GetFileName(item.Key));

                    row.SubItems.Add(
                        item.Value.ToString(
                            Localization.CurrentCulture));

                    row.SubItems.Add(
                        lastShownText);

                    row.Tag = item.Key;

                    statisticsList.Items.Add(row);

                    rank++;
                }

                if (statisticsList.Items.Count == 0)
                {
                    ListViewItem empty =
                        new ListViewItem("");

                    empty.SubItems.Add(
                        Localization.Get(
                            "StatisticsEmpty"));

                    empty.SubItems.Add("");
                    empty.SubItems.Add("");

                    statisticsList.Items.Add(empty);
                }
            }
            finally
            {
                statisticsList.EndUpdate();
            }

            statisticsList.SelectedItems.Clear();
        }

        private void StatisticsList_MouseMove(
            object? sender,
            MouseEventArgs e)
        {
            ListViewHitTestInfo hit =
                statisticsList.HitTest(e.Location);

            if (hit.Item == null ||
                hit.SubItem == null ||
                hit.Item.SubItems.IndexOf(
                    hit.SubItem) != 1 ||
                hit.Item.Tag is not string path ||
                !File.Exists(path))
            {
                HideWallpaperPreview();
                return;
            }

            if (string.Equals(
                previewPath,
                path,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            previewPath = path;

            UpdateWallpaperPreview(path);

            Rectangle subItemBounds =
                hit.SubItem.Bounds;

            Point screenPoint =
                statisticsList.PointToScreen(
                    new Point(
                        subItemBounds.Right + 8,
                        subItemBounds.Top));

            Screen screen =
                Screen.FromControl(this);

            Rectangle area =
                screen.WorkingArea;

            int x = screenPoint.X;
            int y = screenPoint.Y;

            if (x + wallpaperPreviewForm.Width >
                area.Right)
            {
                x =
                    statisticsList
                        .PointToScreen(
                            new Point(
                                subItemBounds.Left,
                                subItemBounds.Top))
                        .X
                    - wallpaperPreviewForm.Width
                    - 8;
            }

            if (y + wallpaperPreviewForm.Height >
                area.Bottom)
            {
                y =
                    area.Bottom
                    - wallpaperPreviewForm.Height;
            }

            if (y < area.Top)
            {
                y = area.Top;
            }

            wallpaperPreviewForm.Location =
                new Point(x, y);

            if (!wallpaperPreviewForm.Visible)
            {
                wallpaperPreviewForm.Show(this);
            }

            if (wallpaperPreviewForm.IsHandleCreated)
            {
                ShowWindow(
                    wallpaperPreviewForm.Handle,
                    SW_SHOWNOACTIVATE);
            }
        }

        private void StatisticsList_MouseClick(
            object? sender,
            MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            ListViewHitTestInfo hit =
                statisticsList.HitTest(e.Location);

            if (hit.Item == null ||
                hit.SubItem == null ||
                hit.Item.SubItems.IndexOf(
                    hit.SubItem) != 1 ||
                hit.Item.Tag is not string path ||
                !File.Exists(path))
            {
                return;
            }

            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
            }
            catch
            {
            }
        }

        private void UpdateWallpaperPreview(
            string path)
        {
            try
            {
                Image? oldImage =
                    wallpaperPreviewPictureBox.Image;

                using (Image source =
                    Image.FromFile(path))
                {
                    wallpaperPreviewPictureBox.Image =
                        new Bitmap(source);
                }

                oldImage?.Dispose();

                FileInfo fileInfo =
                    new FileInfo(path);

                string resolution =
                    GetImageResolutionText(path);

                string sizeText =
                    FormatFileSize(
                        fileInfo.Length);

                wallpaperPreviewInfoLabel.Text =
                    $"{Path.GetFileName(path)}\n" +
                    $"{resolution}   •   {sizeText}   •   " +
                    fileInfo.LastWriteTime.ToString(
                        "g",
                        Localization.CurrentCulture) +
                    "\n" +
                    path;
            }
            catch
            {
                wallpaperPreviewInfoLabel.Text =
                    path;
            }
        }

        private void HideWallpaperPreview()
        {
            previewPath = null;

            if (wallpaperPreviewForm.IsHandleCreated &&
                wallpaperPreviewForm.Visible)
            {
                ShowWindow(
                    wallpaperPreviewForm.Handle,
                    SW_HIDE);
            }
        }

        private static string GetImageResolutionText(
            string path)
        {
            try
            {
                using Image image =
                    Image.FromFile(path);

                return
                    $"{image.Width} × {image.Height}";
            }
            catch
            {
                return Localization.Get(
                    "NotAvailable");
            }
        }

        private static string FormatFileSize(
            long bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;

            if (bytes >= GB)
            {
                return
                    $"{bytes / GB:0.##} GB";
            }

            if (bytes >= MB)
            {
                return
                    $"{bytes / MB:0.##} MB";
            }

            if (bytes >= KB)
            {
                return
                    $"{bytes / KB:0.##} KB";
            }

            return
                $"{bytes} B";
        }

        private void ApplyTheme()
        {
            Color background =
                darkMode
                ? Color.FromArgb(32, 32, 32)
                : SystemColors.Control;

            Color foreground =
                darkMode
                ? Color.FromArgb(
                    235, 235, 235)
                : SystemColors.ControlText;

            Color inputBackground =
                darkMode
                ? Color.FromArgb(
                    48, 48, 48)
                : SystemColors.Window;

            BackColor = background;
            ForeColor = foreground;

            foreach (Control control in Controls)
            {
                control.ForeColor = foreground;

                if (control is ComboBox combo)
                {
                    combo.BackColor =
                        inputBackground;
                }
            }

            statisticsList.BackColor =
                inputBackground;

            statisticsList.ForeColor =
                foreground;

            wallpaperPreviewForm.BackColor =
                darkMode
                ? Color.FromArgb(28, 28, 28)
                : Color.White;

            wallpaperPreviewForm.ForeColor =
                foreground;

            wallpaperPreviewPictureBox.BackColor =
                Color.Black;

            wallpaperPreviewInfoLabel.ForeColor =
                foreground;

            closeButton.UseVisualStyleBackColor =
                false;

            closeButton.BackColor =
                darkMode
                ? Color.FromArgb(
                    50, 50, 50)
                : SystemColors.Control;

            closeButton.ForeColor =
                foreground;

            closeButton.FlatStyle =
                FlatStyle.Flat;

            closeButton.FlatAppearance.BorderColor =
                darkMode
                ? Color.FromArgb(
                    85, 85, 85)
                : Color.FromArgb(
                    180, 180, 180);

            ApplyTitleBarTheme();
        }

        private void ApplyTitleBarTheme()
        {
            if (!IsHandleCreated)
                return;

            int value =
                darkMode ? 1 : 0;

            DwmSetWindowAttribute(
                Handle,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref value,
                sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int
            DwmSetWindowAttribute(
                IntPtr hwnd,
                int attribute,
                ref int attributeValue,
                int attributeSize);

        [DllImport("user32.dll")]
        private static extern bool
            ShowWindow(
                IntPtr hWnd,
                int nCmdShow);
    }
}
