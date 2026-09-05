using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class StatisticsForm : Form
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_HIDE = 0;

        private readonly IReadOnlyDictionary<string, int> wallpaperViewCounts;
        private readonly IReadOnlyDictionary<string, DateTime> wallpaperLastShown;
        private readonly IReadOnlyDictionary<string, Dictionary<string, int>>
            wallpaperDailyViewCounts;
        private DateTime statisticsStartedAt;
        private DateTime dailyStatisticsStartedAt;
        private readonly Action<string>? removeFromStatistics;
        private readonly Action<string>? setWallpaper;
        private readonly Action? resetStatistics;

        private readonly Label summaryLabel;
        private readonly MetricCard mostViewedCard;
        private readonly MetricCard leastViewedCard;
        private readonly MetricCard averageCard;
        private readonly MetricCard fairnessCard;
        private readonly ToolTip dashboardToolTip;
        private readonly ComboBox filterComboBox;
        private readonly ComboBox periodComboBox;
        private readonly TextBox searchTextBox;
        private readonly DoubleBufferedListView statisticsList;
        private readonly Button resetButton;
        private readonly Button closeButton;

        private readonly Form wallpaperPreviewForm;
        private readonly PictureBox wallpaperPreviewPictureBox;
        private readonly Label wallpaperPreviewInfoLabel;

        private readonly ContextMenuStrip rowContextMenu;
        private readonly ToolStripMenuItem setWallpaperMenuItem;
        private readonly ToolStripMenuItem openMenuItem;
        private readonly ToolStripMenuItem openFolderMenuItem;
        private readonly ToolStripMenuItem copyPathMenuItem;
        private readonly ToolStripMenuItem removeStatisticsMenuItem;

        private readonly ImageList rowHeightImageList;
        private readonly Dictionary<string, Image> thumbnailCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> thumbnailLoading =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly bool darkMode;

        private string? previewPath;
        private int hoveredItemIndex = -1;
        private SortColumn sortColumn = SortColumn.Views;
        private bool sortAscending = false;

        private enum SortColumn
        {
            Wallpaper,
            Views,
            Share,
            LastShown
        }

        private sealed class MetricCard : Panel
        {
            public Label TitleLabel { get; }
            public Label ValueLabel { get; }
            public Label DetailLabel { get; }

            public MetricCard()
            {
                Size = new Size(205, 88);
                Padding = new Padding(12);
                BorderStyle = BorderStyle.FixedSingle;

                TitleLabel = new Label
                {
                    Location = new Point(12, 9),
                    Size = new Size(179, 20),
                    Font = new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Regular),
                    AutoEllipsis = true
                };

                ValueLabel = new Label
                {
                    Location = new Point(12, 30),
                    Size = new Size(179, 28),
                    Font = new Font(
                        "Segoe UI",
                        15,
                        FontStyle.Bold),
                    AutoEllipsis = true
                };

                DetailLabel = new Label
                {
                    Location = new Point(12, 60),
                    Size = new Size(179, 18),
                    Font = new Font(
                        "Segoe UI",
                        8,
                        FontStyle.Regular),
                    AutoEllipsis = true
                };

                Controls.Add(TitleLabel);
                Controls.Add(ValueLabel);
                Controls.Add(DetailLabel);
            }

            public void SetColors(
                Color background,
                Color foreground,
                Color muted)
            {
                BackColor = background;
                ForeColor = foreground;

                TitleLabel.BackColor = background;
                ValueLabel.BackColor = background;
                DetailLabel.BackColor = background;

                TitleLabel.ForeColor = muted;
                ValueLabel.ForeColor = foreground;
                DetailLabel.ForeColor = muted;
            }
        }

        private enum StatisticsPeriod
        {
            All,
            Today,
            Yesterday,
            Last7Days,
            Last30Days
        }

        private sealed class PeriodChoice
        {
            public string Text { get; }
            public StatisticsPeriod Period { get; }

            public PeriodChoice(
                string text,
                StatisticsPeriod period)
            {
                Text = text;
                Period = period;
            }

            public override string ToString() => Text;
        }

        private sealed class FilterChoice
        {
            public string Text { get; }
            public int? MaxItems { get; }

            public FilterChoice(string text, int? maxItems)
            {
                Text = text;
                MaxItems = maxItems;
            }

            public override string ToString() => Text;
        }

        private sealed class RowData
        {
            public string Path { get; }
            public int Views { get; }
            public DateTime LastShown { get; }
            public double Share { get; }
            public int PopularityRank { get; }
            public bool Exists { get; }

            public RowData(
                string path,
                int views,
                DateTime lastShown,
                double share,
                int popularityRank)
            {
                Path = path;
                Views = views;
                LastShown = lastShown;
                Share = share;
                PopularityRank = popularityRank;
                Exists = File.Exists(path);
            }
        }

        private sealed class DoubleBufferedListView : ListView
        {
            public DoubleBufferedListView()
            {
                DoubleBuffered = true;
                SetStyle(
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint,
                    true);
            }
        }

        private sealed class StatisticsMenuColorTable :
            ProfessionalColorTable
        {
            private readonly bool dark;

            public StatisticsMenuColorTable(
                bool dark)
            {
                this.dark = dark;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground =>
                dark
                    ? Color.FromArgb(36, 36, 36)
                    : Color.White;

            public override Color ImageMarginGradientBegin =>
                ToolStripDropDownBackground;

            public override Color ImageMarginGradientMiddle =>
                ToolStripDropDownBackground;

            public override Color ImageMarginGradientEnd =>
                ToolStripDropDownBackground;

            public override Color MenuItemSelected =>
                dark
                    ? Color.FromArgb(58, 58, 58)
                    : Color.FromArgb(232, 240, 248);

            public override Color MenuItemBorder =>
                dark
                    ? Color.FromArgb(80, 80, 80)
                    : Color.FromArgb(190, 205, 220);

            public override Color MenuBorder =>
                dark
                    ? Color.FromArgb(92, 92, 92)
                    : Color.FromArgb(175, 175, 175);

            public override Color SeparatorDark =>
                dark
                    ? Color.FromArgb(74, 74, 74)
                    : Color.FromArgb(205, 205, 205);

            public override Color SeparatorLight =>
                SeparatorDark;

            public override Color MenuItemPressedGradientBegin =>
                MenuItemSelected;

            public override Color MenuItemPressedGradientMiddle =>
                MenuItemSelected;

            public override Color MenuItemPressedGradientEnd =>
                MenuItemSelected;

            public override Color MenuItemSelectedGradientBegin =>
                MenuItemSelected;

            public override Color MenuItemSelectedGradientEnd =>
                MenuItemSelected;
        }

        public StatisticsForm(
            bool darkMode,
            int windowOpacityPercent,
            IReadOnlyDictionary<string, int> wallpaperViewCounts,
            IReadOnlyDictionary<string, DateTime> wallpaperLastShown,
            IReadOnlyDictionary<string, Dictionary<string, int>>
                wallpaperDailyViewCounts,
            DateTime statisticsStartedAt,
            DateTime dailyStatisticsStartedAt,
            Action<string>? removeFromStatistics = null,
            Action<string>? setWallpaper = null,
            Action? resetStatistics = null)
        {
            this.darkMode = darkMode;
            this.wallpaperViewCounts = wallpaperViewCounts;
            this.wallpaperLastShown = wallpaperLastShown;
            this.wallpaperDailyViewCounts = wallpaperDailyViewCounts;
            this.statisticsStartedAt = statisticsStartedAt;
            this.dailyStatisticsStartedAt = dailyStatisticsStartedAt;
            this.removeFromStatistics = removeFromStatistics;
            this.setWallpaper = setWallpaper;
            this.resetStatistics = resetStatistics;

            Text = Localization.Get("StatisticsTitle");

            Icon = Icon.ExtractAssociatedIcon(
                Application.ExecutablePath);

            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            ClientSize = new Size(920, 715);
            Font = new Font("Segoe UI", 10);

            Opacity =
                Math.Clamp(
                    windowOpacityPercent,
                    80,
                    100) / 100.0;

            Label titleLabel = new Label
            {
                Text = Localization.Get("StatisticsTitle"),
                Location = new Point(25, 18),
                AutoSize = true,
                Font = new Font(
                    "Segoe UI",
                    15,
                    FontStyle.Bold)
            };

            summaryLabel = new Label
            {
                Location = new Point(25, 55),
                Size = new Size(870, 24)
            };

            dashboardToolTip = new ToolTip
            {
                AutoPopDelay = 9000,
                InitialDelay = 400,
                ReshowDelay = 100,
                ShowAlways = true
            };

            mostViewedCard = new MetricCard
            {
                Location = new Point(25, 88)
            };

            mostViewedCard.TitleLabel.Text =
                Localization.Get(
                    "StatisticsMetricMostViewed");

            leastViewedCard = new MetricCard
            {
                Location = new Point(242, 88)
            };

            leastViewedCard.TitleLabel.Text =
                Localization.Get(
                    "StatisticsMetricLeastViewed");

            averageCard = new MetricCard
            {
                Location = new Point(459, 88)
            };

            averageCard.TitleLabel.Text =
                Localization.Get(
                    "StatisticsMetricAverage");

            fairnessCard = new MetricCard
            {
                Location = new Point(676, 88)
            };

            fairnessCard.TitleLabel.Text =
                Localization.Get(
                    "StatisticsMetricFairness");

            string fairnessToolTipText =
                Localization.Get(
                    "StatisticsFairnessToolTip");

            dashboardToolTip.SetToolTip(
                fairnessCard,
                fairnessToolTipText);

            dashboardToolTip.SetToolTip(
                fairnessCard.TitleLabel,
                fairnessToolTipText);

            dashboardToolTip.SetToolTip(
                fairnessCard.ValueLabel,
                fairnessToolTipText);

            dashboardToolTip.SetToolTip(
                fairnessCard.DetailLabel,
                fairnessToolTipText);

            Label filterLabel = new Label
            {
                Text = Localization.Get(
                    "StatisticsFilterLabel"),
                Location = new Point(25, 200),
                Size = new Size(105, 25)
            };

            filterComboBox = new ComboBox
            {
                Location = new Point(130, 195),
                Size = new Size(150, 28),
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            filterComboBox.Items.Add(
                new FilterChoice(
                    Localization.Get(
                        "StatisticsFilterTop10"),
                    10));

            filterComboBox.Items.Add(
                new FilterChoice(
                    Localization.Get(
                        "StatisticsFilterTop25"),
                    25));

            filterComboBox.Items.Add(
                new FilterChoice(
                    Localization.Get(
                        "StatisticsFilterAll"),
                    null));

            filterComboBox.SelectedIndex = 0;

            filterComboBox.SelectedIndexChanged +=
                (_, _) => RefreshStatistics();

            Label periodLabel = new Label
            {
                Text = Localization.Get(
                    "StatisticsPeriodLabel"),
                Location = new Point(300, 200),
                Size = new Size(80, 25)
            };

            periodComboBox = new ComboBox
            {
                Location = new Point(380, 195),
                Size = new Size(170, 28),
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            periodComboBox.Items.Add(
                new PeriodChoice(
                    Localization.Get(
                        "StatisticsPeriodAll"),
                    StatisticsPeriod.All));

            periodComboBox.Items.Add(
                new PeriodChoice(
                    Localization.Get(
                        "StatisticsPeriodToday"),
                    StatisticsPeriod.Today));

            periodComboBox.Items.Add(
                new PeriodChoice(
                    Localization.Get(
                        "StatisticsPeriodYesterday"),
                    StatisticsPeriod.Yesterday));

            periodComboBox.Items.Add(
                new PeriodChoice(
                    Localization.Get(
                        "StatisticsPeriodLast7Days"),
                    StatisticsPeriod.Last7Days));

            periodComboBox.Items.Add(
                new PeriodChoice(
                    Localization.Get(
                        "StatisticsPeriodLast30Days"),
                    StatisticsPeriod.Last30Days));

            periodComboBox.SelectedIndex = 0;

            periodComboBox.SelectedIndexChanged +=
                (_, _) => RefreshStatistics();

            searchTextBox = new TextBox
            {
                Location = new Point(590, 195),
                Size = new Size(305, 27),
                PlaceholderText =
                    Localization.Get(
                        "StatisticsSearchPlaceholder")
            };

            searchTextBox.TextChanged +=
                (_, _) => RefreshStatistics();

            statisticsList = new DoubleBufferedListView
            {
                Location = new Point(25, 239),
                Size = new Size(870, 390),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                HideSelection = true,
                HeaderStyle = ColumnHeaderStyle.Clickable,
                TabStop = false,
                OwnerDraw = true,
                ShowItemToolTips = true
            };

            statisticsList.Columns.Add(
                "",
                100,
                HorizontalAlignment.Left);

            statisticsList.Columns.Add(
                Localization.Get("StatisticsRank"),
                45,
                HorizontalAlignment.Left);

            statisticsList.Columns.Add(
                Localization.Get("StatisticsWallpaper"),
                315,
                HorizontalAlignment.Left);

            statisticsList.Columns.Add(
                Localization.Get("StatisticsViews"),
                110,
                HorizontalAlignment.Right);

            statisticsList.Columns.Add(
                Localization.Get(
                    "StatisticsShare"),
                85,
                HorizontalAlignment.Right);

            statisticsList.Columns.Add(
                Localization.Get("StatisticsLastShown"),
                190,
                HorizontalAlignment.Left);

            rowHeightImageList = new ImageList
            {
                ImageSize = new Size(80, 46),
                ColorDepth = ColorDepth.Depth32Bit
            };

            rowHeightImageList.Images.Add(
                new Bitmap(80, 46));

            statisticsList.SmallImageList =
                rowHeightImageList;

            statisticsList.DrawColumnHeader +=
                StatisticsList_DrawColumnHeader;

            statisticsList.DrawSubItem +=
                StatisticsList_DrawSubItem;

            statisticsList.ColumnClick +=
                StatisticsList_ColumnClick;

            statisticsList.MouseMove +=
                StatisticsList_MouseMove;

            statisticsList.MouseLeave +=
                StatisticsList_MouseLeave;

            statisticsList.MouseDown +=
                StatisticsList_MouseDown;

            statisticsList.MouseDoubleClick +=
                StatisticsList_MouseDoubleClick;

            rowContextMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false,
                AutoSize = true,
                Padding = new Padding(2),
                Font = new Font(
                    "Segoe UI",
                    9.5f,
                    FontStyle.Regular),
                Renderer =
                    new ToolStripProfessionalRenderer(
                        new StatisticsMenuColorTable(
                            darkMode))
            };

            setWallpaperMenuItem =
                new ToolStripMenuItem(
                    Localization.Get(
                        "StatisticsSetWallpaper"));

            openMenuItem = new ToolStripMenuItem(
                Localization.Get(
                    "StatisticsOpen"));

            openFolderMenuItem = new ToolStripMenuItem(
                Localization.Get(
                    "StatisticsOpenFolder"));

            copyPathMenuItem = new ToolStripMenuItem(
                Localization.Get(
                    "StatisticsCopyPath"));

            removeStatisticsMenuItem =
                new ToolStripMenuItem(
                    Localization.Get(
                        "StatisticsRemove"));

            setWallpaperMenuItem.Click +=
                (_, _) => SetSelectedWallpaper();

            openMenuItem.Click +=
                (_, _) => OpenSelectedWallpaper();

            openFolderMenuItem.Click +=
                (_, _) => OpenSelectedWallpaperFolder();

            copyPathMenuItem.Click +=
                (_, _) => CopySelectedWallpaperPath();

            removeStatisticsMenuItem.Click +=
                (_, _) => RemoveSelectedWallpaperFromStatistics();

            rowContextMenu.Items.AddRange(
                new ToolStripItem[]
                {
                    setWallpaperMenuItem,
                    new ToolStripSeparator(),
                    openMenuItem,
                    openFolderMenuItem,
                    new ToolStripSeparator(),
                    copyPathMenuItem,
                    new ToolStripSeparator(),
                    removeStatisticsMenuItem
                });

            foreach (ToolStripItem item in
                rowContextMenu.Items)
            {
                if (item is ToolStripMenuItem menuItem)
                {
                    menuItem.AutoSize = true;
                    menuItem.Padding =
                        new Padding(10, 5, 12, 5);
                    menuItem.Margin =
                        new Padding(0);
                }
                else if (item is ToolStripSeparator separator)
                {
                    separator.Margin =
                        new Padding(6, 2, 6, 2);
                }
            }

            setWallpaperMenuItem.Font =
                new Font(
                    rowContextMenu.Font,
                    FontStyle.Bold);

            removeStatisticsMenuItem.ForeColor =
                darkMode
                    ? Color.FromArgb(235, 150, 150)
                    : Color.FromArgb(165, 45, 45);

            rowContextMenu.Opening +=
                (_, e) =>
                {
                    RowData? row = GetSelectedRow();

                    if (row == null)
                    {
                        e.Cancel = true;
                        return;
                    }

                    setWallpaperMenuItem.Enabled =
                        row.Exists &&
                        setWallpaper != null;

                    openMenuItem.Enabled = row.Exists;

                    string? directory =
                        Path.GetDirectoryName(row.Path);

                    openFolderMenuItem.Enabled =
                        !string.IsNullOrWhiteSpace(directory) &&
                        Directory.Exists(directory);

                    copyPathMenuItem.Enabled = true;
                    removeStatisticsMenuItem.Enabled =
                        removeFromStatistics != null;
                };

            statisticsList.ContextMenuStrip =
                rowContextMenu;

            resetButton = new Button
            {
                Text =
                    Localization.Get(
                        "StatisticsResetButton"),
                Location = new Point(25, 650),
                Size = new Size(190, 34)
            };

            resetButton.Click +=
                ResetButton_Click;

            closeButton = new Button
            {
                Text = Localization.Get("AboutClose"),
                Location = new Point(770, 650),
                Size = new Size(125, 34),
                DialogResult = DialogResult.OK
            };

            wallpaperPreviewForm = new PreviewForm
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                ClientSize = new Size(420, 300),
                Padding = new Padding(8)
            };

            wallpaperPreviewPictureBox =
                new PictureBox
                {
                    Location = new Point(8, 8),
                    Size = new Size(404, 228),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Black
                };

            wallpaperPreviewInfoLabel =
                new Label
                {
                    Location = new Point(8, 244),
                    Size = new Size(404, 48),
                    AutoEllipsis = true
                };

            wallpaperPreviewForm.Controls.Add(
                wallpaperPreviewPictureBox);

            wallpaperPreviewForm.Controls.Add(
                wallpaperPreviewInfoLabel);

            Controls.Add(titleLabel);
            Controls.Add(summaryLabel);
            Controls.Add(mostViewedCard);
            Controls.Add(leastViewedCard);
            Controls.Add(averageCard);
            Controls.Add(fairnessCard);
            Controls.Add(filterLabel);
            Controls.Add(filterComboBox);
            Controls.Add(periodLabel);
            Controls.Add(periodComboBox);
            Controls.Add(searchTextBox);
            Controls.Add(statisticsList);
            Controls.Add(resetButton);
            Controls.Add(closeButton);

            AcceptButton = closeButton;
            CancelButton = closeButton;

            ApplyTheme();
            UpdateColumnHeaders();
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

            Image? oldPreview =
                wallpaperPreviewPictureBox.Image;

            wallpaperPreviewPictureBox.Image = null;
            oldPreview?.Dispose();

            foreach (Image image in
                thumbnailCache.Values)
            {
                image.Dispose();
            }

            thumbnailCache.Clear();

            rowHeightImageList.Dispose();
            rowContextMenu.Dispose();
            dashboardToolTip.Dispose();
            wallpaperPreviewForm.Dispose();

            base.OnFormClosed(e);
        }

        private void RefreshStatistics()
        {
            PeriodChoice periodChoice =
                periodComboBox.SelectedItem
                    as PeriodChoice ??
                new PeriodChoice(
                    Localization.Get(
                        "StatisticsPeriodAll"),
                    StatisticsPeriod.All);

            Dictionary<string, int> activeViewCounts =
                GetViewCountsForPeriod(
                    periodChoice.Period);

            int totalViews =
                activeViewCounts.Values.Sum();

            double average =
                activeViewCounts.Count == 0
                    ? 0
                    : (double)totalViews /
                      activeViewCounts.Count;

            if (periodChoice.Period ==
                StatisticsPeriod.All)
            {
                summaryLabel.Text =
                    string.Format(
                        Localization.CurrentCulture,
                        Localization.Get(
                            "StatisticsSummaryCompact"),
                        statisticsStartedAt,
                        totalViews,
                        activeViewCounts.Count);
            }
            else
            {
                summaryLabel.Text =
                    string.Format(
                        Localization.CurrentCulture,
                        Localization.Get(
                            "StatisticsPeriodSummary"),
                        periodChoice.Text,
                        totalViews,
                        activeViewCounts.Count,
                        dailyStatisticsStartedAt);
            }

            UpdateDashboardCards(
                activeViewCounts,
                totalViews,
                average);

            List<RowData> popularityOrder =
                activeViewCounts
                    .OrderByDescending(
                        item => item.Value)
                    .ThenBy(
                        item =>
                            Path.GetFileName(
                                item.Key),
                        StringComparer
                            .CurrentCultureIgnoreCase)
                    .Select(
                        (item, index) =>
                        {
                            wallpaperLastShown.TryGetValue(
                                item.Key,
                                out DateTime lastShown);

                            double share =
                                totalViews <= 0
                                    ? 0
                                    : item.Value * 100.0 /
                                      totalViews;

                            return new RowData(
                                item.Key,
                                item.Value,
                                lastShown,
                                share,
                                index + 1);
                        })
                    .ToList();

            IEnumerable<RowData> items =
                popularityOrder;

            string search =
                searchTextBox.Text.Trim();

            if (!string.IsNullOrWhiteSpace(search))
            {
                items = items.Where(
                    item =>
                        Path.GetFileName(item.Path)
                            .Contains(
                                search,
                                StringComparison
                                    .CurrentCultureIgnoreCase) ||
                        item.Path.Contains(
                            search,
                            StringComparison
                                .CurrentCultureIgnoreCase));
            }

            if (filterComboBox.SelectedItem
                is FilterChoice choice &&
                choice.MaxItems.HasValue)
            {
                int maxRank =
                    choice.MaxItems.Value;

                items = items.Where(
                    item =>
                        item.PopularityRank <=
                        maxRank);
            }

            items = ApplySorting(items);

            statisticsList.BeginUpdate();

            try
            {
                statisticsList.Items.Clear();

                foreach (RowData item in items)
                {
                    string fileName =
                        Path.GetFileName(item.Path);

                    if (!item.Exists)
                    {
                        fileName =
                            "⚠ " +
                            fileName +
                            "  [" +
                            Localization.Get(
                                "StatisticsNotFound") +
                            "]";
                    }

                    string lastShownText =
                        item.LastShown == default
                            ? ""
                            : item.LastShown.ToString(
                                "g",
                                Localization.CurrentCulture);

                    ListViewItem row =
                        new ListViewItem("");

                    row.SubItems.Add(
                        item.PopularityRank.ToString(
                            Localization.CurrentCulture));

                    row.SubItems.Add(fileName);

                    row.SubItems.Add(
                        item.Views.ToString(
                            "N0",
                            Localization.CurrentCulture));

                    row.SubItems.Add(
                        item.Share.ToString(
                            "0.0",
                            Localization.CurrentCulture) +
                        " %");

                    row.SubItems.Add(
                        lastShownText);

                    row.Tag = item;
                    row.ToolTipText = item.Path;

                    statisticsList.Items.Add(row);

                    if (item.Exists)
                    {
                        QueueThumbnailLoad(item.Path);
                    }
                }

                if (statisticsList.Items.Count == 0)
                {
                    ListViewItem empty =
                        new ListViewItem("");

                    empty.SubItems.Add("");
                    empty.SubItems.Add(
                        Localization.Get(
                            "StatisticsEmpty"));
                    empty.SubItems.Add("");
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
            hoveredItemIndex = -1;
        }

        private Dictionary<string, int> GetViewCountsForPeriod(
            StatisticsPeriod period)
        {
            if (period == StatisticsPeriod.All)
            {
                return wallpaperViewCounts
                    .Where(item => item.Value > 0)
                    .ToDictionary(
                        item => item.Key,
                        item => item.Value,
                        StringComparer.OrdinalIgnoreCase);
            }

            DateTime today =
                DateTime.Today;

            DateTime startDate;
            DateTime endDate;

            switch (period)
            {
                case StatisticsPeriod.Today:
                    startDate = today;
                    endDate = today;
                    break;

                case StatisticsPeriod.Yesterday:
                    startDate = today.AddDays(-1);
                    endDate = startDate;
                    break;

                case StatisticsPeriod.Last7Days:
                    startDate = today.AddDays(-6);
                    endDate = today;
                    break;

                case StatisticsPeriod.Last30Days:
                    startDate = today.AddDays(-29);
                    endDate = today;
                    break;

                default:
                    return wallpaperViewCounts
                        .Where(item => item.Value > 0)
                        .ToDictionary(
                            item => item.Key,
                            item => item.Value,
                            StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, int> result =
                new(StringComparer.OrdinalIgnoreCase);

            foreach ((string path,
                Dictionary<string, int> dailyCounts)
                in wallpaperDailyViewCounts)
            {
                int sum = 0;

                foreach ((string dateKey, int count)
                    in dailyCounts)
                {
                    if (count <= 0)
                    {
                        continue;
                    }

                    if (!DateTime.TryParseExact(
                        dateKey,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime date))
                    {
                        continue;
                    }

                    date = date.Date;

                    if (date < startDate ||
                        date > endDate)
                    {
                        continue;
                    }

                    sum += count;
                }

                if (sum > 0)
                {
                    result[path] = sum;
                }
            }

            return result;
        }

        private void UpdateDashboardCards(
            IReadOnlyDictionary<string, int> activeViewCounts,
            int totalViews,
            double average)
        {
            if (activeViewCounts.Count == 0 ||
                totalViews <= 0)
            {
                string noData =
                    Localization.Get(
                        "StatisticsMetricNoData");

                SetMetricCard(
                    mostViewedCard,
                    "–",
                    noData);

                SetMetricCard(
                    leastViewedCard,
                    "–",
                    noData);

                SetMetricCard(
                    averageCard,
                    "0.00",
                    Localization.Get(
                        "StatisticsMetricAverageDetail"));

                SetMetricCard(
                    fairnessCard,
                    "–",
                    Localization.Get(
                        "StatisticsMetricFairnessDetail"));

                return;
            }

            KeyValuePair<string, int> mostViewed =
                activeViewCounts
                    .OrderByDescending(
                        item => item.Value)
                    .ThenBy(
                        item =>
                            Path.GetFileName(
                                item.Key),
                        StringComparer
                            .CurrentCultureIgnoreCase)
                    .First();

            KeyValuePair<string, int> leastViewed =
                activeViewCounts
                    .OrderBy(
                        item => item.Value)
                    .ThenBy(
                        item =>
                            Path.GetFileName(
                                item.Key),
                        StringComparer
                            .CurrentCultureIgnoreCase)
                    .First();

            SetMetricCard(
                mostViewedCard,
                string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsMetricViews"),
                    mostViewed.Value),
                Path.GetFileName(
                    mostViewed.Key));

            SetMetricCard(
                leastViewedCard,
                string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsMetricViews"),
                    leastViewed.Value),
                Path.GetFileName(
                    leastViewed.Key));

            SetMetricCard(
                averageCard,
                average.ToString(
                    "0.00",
                    Localization.CurrentCulture),
                Localization.Get(
                    "StatisticsMetricAverageDetail"));

            double fairness =
                CalculateFairnessScore(
                    activeViewCounts.Values,
                    totalViews);

            SetMetricCard(
                fairnessCard,
                fairness.ToString(
                    "0.0",
                    Localization.CurrentCulture) +
                " %",
                Localization.Get(
                    "StatisticsMetricFairnessDetail"));
        }

        private static void SetMetricCard(
            MetricCard card,
            string value,
            string detail)
        {
            card.ValueLabel.Text = value;
            card.DetailLabel.Text = detail;
        }

        private static double CalculateFairnessScore(
            IEnumerable<int> viewCounts,
            int totalViews)
        {
            int[] counts =
                viewCounts
                    .Where(count => count > 0)
                    .ToArray();

            int wallpaperCount =
                counts.Length;

            if (wallpaperCount <= 1 ||
                totalViews <= 0)
            {
                return 100.0;
            }

            double expectedShare =
                1.0 / wallpaperCount;

            double totalVariation = 0.0;

            foreach (int count in counts)
            {
                double actualShare =
                    (double)count / totalViews;

                totalVariation +=
                    Math.Abs(
                        actualShare -
                        expectedShare);
            }

            totalVariation *= 0.5;

            double maximumVariation =
                1.0 -
                expectedShare;

            if (maximumVariation <= 0)
            {
                return 100.0;
            }

            double normalizedDeviation =
                totalVariation /
                maximumVariation;

            return Math.Clamp(
                (1.0 - normalizedDeviation) *
                100.0,
                0.0,
                100.0);
        }

        private IEnumerable<RowData> ApplySorting(
            IEnumerable<RowData> items)
        {
            StringComparer nameComparer =
                StringComparer.CurrentCultureIgnoreCase;

            IOrderedEnumerable<RowData> ordered;

            switch (sortColumn)
            {
                case SortColumn.Wallpaper:
                    ordered = sortAscending
                        ? items.OrderBy(
                            item =>
                                Path.GetFileName(item.Path),
                            nameComparer)
                        : items.OrderByDescending(
                            item =>
                                Path.GetFileName(item.Path),
                            nameComparer);
                    break;

                case SortColumn.LastShown:
                    ordered = sortAscending
                        ? items.OrderBy(
                            item => item.LastShown)
                        : items.OrderByDescending(
                            item => item.LastShown);
                    break;

                case SortColumn.Share:
                    ordered = sortAscending
                        ? items.OrderBy(
                            item => item.Share)
                        : items.OrderByDescending(
                            item => item.Share);
                    break;

                case SortColumn.Views:
                default:
                    ordered = sortAscending
                        ? items.OrderBy(
                            item => item.Views)
                        : items.OrderByDescending(
                            item => item.Views);
                    break;
            }

            return ordered.ThenBy(
                item => Path.GetFileName(item.Path),
                nameComparer);
        }

        private void StatisticsList_ColumnClick(
            object? sender,
            ColumnClickEventArgs e)
        {
            SortColumn? clicked =
                e.Column switch
                {
                    2 => SortColumn.Wallpaper,
                    3 => SortColumn.Views,
                    4 => SortColumn.Share,
                    5 => SortColumn.LastShown,
                    _ => null
                };

            if (!clicked.HasValue)
                return;

            if (sortColumn == clicked.Value)
            {
                sortAscending = !sortAscending;
            }
            else
            {
                sortColumn = clicked.Value;

                sortAscending =
                    clicked.Value ==
                    SortColumn.Wallpaper;
            }

            UpdateColumnHeaders();
            RefreshStatistics();
        }

        private void UpdateColumnHeaders()
        {
            statisticsList.Columns[2].Text =
                BuildSortableHeader(
                    Localization.Get(
                        "StatisticsWallpaper"),
                    SortColumn.Wallpaper);

            statisticsList.Columns[3].Text =
                BuildSortableHeader(
                    Localization.Get(
                        "StatisticsViews"),
                    SortColumn.Views);

            statisticsList.Columns[4].Text =
                BuildSortableHeader(
                    Localization.Get(
                        "StatisticsShare"),
                    SortColumn.Share);

            statisticsList.Columns[5].Text =
                BuildSortableHeader(
                    Localization.Get(
                        "StatisticsLastShown"),
                    SortColumn.LastShown);
        }

        private string BuildSortableHeader(
            string text,
            SortColumn column)
        {
            if (sortColumn != column)
                return text;

            return text +
                (sortAscending ? "  ▲" : "  ▼");
        }

        private void StatisticsList_DrawColumnHeader(
            object? sender,
            DrawListViewColumnHeaderEventArgs e)
        {
            Color backColor =
                darkMode
                    ? Color.FromArgb(42, 42, 42)
                    : Color.FromArgb(240, 240, 240);

            Color foreColor =
                darkMode
                    ? Color.FromArgb(235, 235, 235)
                    : SystemColors.ControlText;

            using SolidBrush brush =
                new(backColor);

            e.Graphics.FillRectangle(
                brush,
                e.Bounds);

            TextFormatFlags flags =
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis;

            if (e.Header.TextAlign ==
                HorizontalAlignment.Right)
            {
                flags |=
                    TextFormatFlags.Right;
            }
            else
            {
                flags |=
                    TextFormatFlags.Left;
            }

            Rectangle textBounds =
                Rectangle.Inflate(
                    e.Bounds,
                    -8,
                    0);

            TextRenderer.DrawText(
                e.Graphics,
                e.Header.Text,
                Font,
                textBounds,
                foreColor,
                flags);

            using Pen linePen =
                new(
                    darkMode
                        ? Color.FromArgb(
                            68, 68, 68)
                        : Color.FromArgb(
                            210, 210, 210));

            e.Graphics.DrawLine(
                linePen,
                e.Bounds.Left,
                e.Bounds.Bottom - 1,
                e.Bounds.Right,
                e.Bounds.Bottom - 1);
        }

        private void StatisticsList_DrawSubItem(
            object? sender,
            DrawListViewSubItemEventArgs e)
        {
            bool isHover =
                e.ItemIndex == hoveredItemIndex;

            bool alternate =
                e.ItemIndex % 2 == 1;

            Color background =
                darkMode
                    ? alternate
                        ? Color.FromArgb(39, 39, 39)
                        : Color.FromArgb(34, 34, 34)
                    : alternate
                        ? Color.FromArgb(248, 248, 248)
                        : Color.White;

            if (isHover)
            {
                background =
                    darkMode
                        ? Color.FromArgb(54, 54, 54)
                        : Color.FromArgb(232, 241, 250);
            }

            using SolidBrush backBrush =
                new(background);

            e.Graphics.FillRectangle(
                backBrush,
                e.Bounds);

            RowData? row =
                e.Item.Tag as RowData;

            Color foreground =
                row != null && !row.Exists
                    ? darkMode
                        ? Color.FromArgb(
                            170, 170, 170)
                        : Color.Gray
                    : darkMode
                        ? Color.FromArgb(
                            235, 235, 235)
                        : SystemColors.ControlText;

            if (e.ColumnIndex == 0)
            {
                DrawThumbnail(
                    e.Graphics,
                    e.Bounds,
                    row);

                return;
            }

            Rectangle textBounds =
                Rectangle.Inflate(
                    e.Bounds,
                    -8,
                    0);

            TextFormatFlags flags =
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine;

            if (statisticsList.Columns[
                    e.ColumnIndex].TextAlign ==
                HorizontalAlignment.Right)
            {
                flags |= TextFormatFlags.Right;
            }
            else
            {
                flags |= TextFormatFlags.Left;
            }

            Font drawFont = Font;

            if (e.ColumnIndex == 2 &&
                row?.Exists == false)
            {
                drawFont =
                    new Font(
                        Font,
                        FontStyle.Italic);
            }

            TextRenderer.DrawText(
                e.Graphics,
                e.SubItem.Text,
                drawFont,
                textBounds,
                foreground,
                flags);

            if (!ReferenceEquals(
                drawFont,
                Font))
            {
                drawFont.Dispose();
            }

            using Pen separatorPen =
                new(
                    darkMode
                        ? Color.FromArgb(
                            50, 50, 50)
                        : Color.FromArgb(
                            235, 235, 235));

            e.Graphics.DrawLine(
                separatorPen,
                e.Bounds.Left,
                e.Bounds.Bottom - 1,
                e.Bounds.Right,
                e.Bounds.Bottom - 1);
        }

        private void DrawThumbnail(
            Graphics graphics,
            Rectangle bounds,
            RowData? row)
        {
            Rectangle imageBounds =
                new(
                    bounds.Left + 9,
                    bounds.Top + 5,
                    80,
                    45);

            using SolidBrush placeholder =
                new(
                    darkMode
                        ? Color.FromArgb(
                            24, 24, 24)
                        : Color.FromArgb(
                            225, 225, 225));

            graphics.FillRectangle(
                placeholder,
                imageBounds);

            if (row == null ||
                !row.Exists ||
                !thumbnailCache.TryGetValue(
                    row.Path,
                    out Image? image))
            {
                using Pen borderPen =
                    new(
                        darkMode
                            ? Color.FromArgb(
                                90, 90, 90)
                            : Color.FromArgb(
                                175, 175, 175));

                graphics.DrawRectangle(
                    borderPen,
                    imageBounds);

                if (row != null && !row.Exists)
                {
                    using Pen crossPen =
                        new(
                            darkMode
                                ? Color.FromArgb(
                                    150, 150, 150)
                                : Color.FromArgb(
                                    115, 115, 115),
                            2);

                    graphics.DrawLine(
                        crossPen,
                        imageBounds.Left + 20,
                        imageBounds.Top + 10,
                        imageBounds.Right - 20,
                        imageBounds.Bottom - 10);

                    graphics.DrawLine(
                        crossPen,
                        imageBounds.Right - 20,
                        imageBounds.Top + 10,
                        imageBounds.Left + 20,
                        imageBounds.Bottom - 10);
                }

                return;
            }

            graphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;

            graphics.DrawImage(
                image,
                imageBounds);

            using Pen imageBorder =
                new(
                    darkMode
                        ? Color.FromArgb(
                            78, 78, 78)
                        : Color.FromArgb(
                            180, 180, 180));

            graphics.DrawRectangle(
                imageBorder,
                imageBounds);
        }

        private void QueueThumbnailLoad(
            string path)
        {
            if (thumbnailCache.ContainsKey(path) ||
                thumbnailLoading.Contains(path))
            {
                return;
            }

            thumbnailLoading.Add(path);

            _ = Task.Run(
                () => CreateThumbnail(path))
                .ContinueWith(
                    task =>
                    {
                        if (IsDisposed ||
                            Disposing ||
                            !IsHandleCreated)
                        {
                            if (task.Status ==
                                TaskStatus.RanToCompletion)
                            {
                                task.Result?.Dispose();
                            }

                            return;
                        }

                        try
                        {
                            BeginInvoke(
                                new Action(
                                    () =>
                                    {
                                    thumbnailLoading.Remove(
                                        path);

                                    if (task.Status !=
                                            TaskStatus.RanToCompletion ||
                                        task.Result == null)
                                    {
                                        return;
                                    }

                                    if (!thumbnailCache
                                        .ContainsKey(path))
                                    {
                                        thumbnailCache[path] =
                                            task.Result;
                                    }
                                    else
                                    {
                                        task.Result.Dispose();
                                    }

                                        statisticsList.Invalidate();
                                    }));
                        }
                        catch
                        {
                            if (task.Status ==
                                TaskStatus.RanToCompletion)
                            {
                                task.Result?.Dispose();
                            }
                        }
                    },
                    TaskScheduler.Default);
        }

        private static Bitmap? CreateThumbnail(
            string path)
        {
            try
            {
                using Image source =
                    Image.FromFile(path);

                Bitmap bitmap =
                    new(80, 45);

                using Graphics graphics =
                    Graphics.FromImage(bitmap);

                graphics.Clear(Color.Black);

                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;

                graphics.SmoothingMode =
                    SmoothingMode.HighQuality;

                graphics.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;

                Rectangle sourceRect =
                    CalculateCropRectangle(
                        source.Width,
                        source.Height,
                        80,
                        45);

                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, 80, 45),
                    sourceRect,
                    GraphicsUnit.Pixel);

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static Rectangle CalculateCropRectangle(
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight)
        {
            double sourceAspect =
                (double)sourceWidth /
                sourceHeight;

            double targetAspect =
                (double)targetWidth /
                targetHeight;

            if (sourceAspect > targetAspect)
            {
                int cropWidth =
                    (int)Math.Round(
                        sourceHeight *
                        targetAspect);

                int x =
                    (sourceWidth -
                     cropWidth) / 2;

                return new Rectangle(
                    x,
                    0,
                    cropWidth,
                    sourceHeight);
            }

            int cropHeight =
                (int)Math.Round(
                    sourceWidth /
                    targetAspect);

            int y =
                (sourceHeight -
                 cropHeight) / 2;

            return new Rectangle(
                0,
                y,
                sourceWidth,
                cropHeight);
        }

        private void StatisticsList_MouseMove(
            object? sender,
            MouseEventArgs e)
        {
            ListViewHitTestInfo hit =
                statisticsList.HitTest(e.Location);

            int newHoverIndex =
                hit.Item?.Index ?? -1;

            if (newHoverIndex !=
                hoveredItemIndex)
            {
                int oldHover =
                    hoveredItemIndex;

                hoveredItemIndex =
                    newHoverIndex;

                if (oldHover >= 0 &&
                    oldHover <
                    statisticsList.Items.Count)
                {
                    statisticsList.Invalidate(
                        statisticsList.Items[
                            oldHover].Bounds);
                }

                if (hoveredItemIndex >= 0 &&
                    hoveredItemIndex <
                    statisticsList.Items.Count)
                {
                    statisticsList.Invalidate(
                        statisticsList.Items[
                            hoveredItemIndex].Bounds);
                }
            }

            if (hit.Item == null ||
                hit.SubItem == null ||
                hit.Item.SubItems.IndexOf(
                    hit.SubItem) != 2 ||
                hit.Item.Tag is not RowData row ||
                !row.Exists)
            {
                HideWallpaperPreview();
                return;
            }

            if (string.Equals(
                previewPath,
                row.Path,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            previewPath = row.Path;

            UpdateWallpaperPreview(
                row.Path);

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

        private void StatisticsList_MouseLeave(
            object? sender,
            EventArgs e)
        {
            if (hoveredItemIndex >= 0 &&
                hoveredItemIndex <
                statisticsList.Items.Count)
            {
                int oldHover =
                    hoveredItemIndex;

                hoveredItemIndex = -1;

                statisticsList.Invalidate(
                    statisticsList.Items[
                        oldHover].Bounds);
            }

            HideWallpaperPreview();
        }

        private void StatisticsList_MouseDown(
            object? sender,
            MouseEventArgs e)
        {
            ListViewHitTestInfo hit =
                statisticsList.HitTest(e.Location);

            if (hit.Item != null)
            {
                hit.Item.Selected = true;
            }
        }

        private void StatisticsList_MouseDoubleClick(
            object? sender,
            MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            ListViewHitTestInfo hit =
                statisticsList.HitTest(e.Location);

            if (hit.Item?.Tag is not RowData row ||
                !row.Exists ||
                setWallpaper == null)
            {
                return;
            }

            hit.Item.Selected = true;
            SetSelectedWallpaper();
        }

        private RowData? GetSelectedRow()
        {
            if (statisticsList.SelectedItems.Count == 0)
                return null;

            return statisticsList
                .SelectedItems[0]
                .Tag as RowData;
        }

        private void SetSelectedWallpaper()
        {
            RowData? row =
                GetSelectedRow();

            if (row?.Exists != true ||
                setWallpaper == null)
            {
                return;
            }

            setWallpaper(row.Path);
        }

        private void OpenSelectedWallpaper()
        {
            RowData? row =
                GetSelectedRow();

            if (row?.Exists == true)
            {
                OpenWallpaper(row.Path);
            }
        }

        private static void OpenWallpaper(
            string path)
        {
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

        private void OpenSelectedWallpaperFolder()
        {
            RowData? row =
                GetSelectedRow();

            if (row == null)
                return;

            string? folder =
                Path.GetDirectoryName(
                    row.Path);

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                return;
            }

            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = folder,
                        UseShellExecute = true
                    });
            }
            catch
            {
            }
        }

        private void CopySelectedWallpaperPath()
        {
            RowData? row =
                GetSelectedRow();

            if (row == null)
                return;

            try
            {
                Clipboard.SetText(row.Path);
            }
            catch
            {
            }
        }

        private void RemoveSelectedWallpaperFromStatistics()
        {
            RowData? row =
                GetSelectedRow();

            if (row == null ||
                removeFromStatistics == null)
            {
                return;
            }

            string message =
                string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsRemoveConfirm"),
                    Path.GetFileName(
                        row.Path));

            DialogResult result =
                MessageBox.Show(
                    this,
                    message,
                    Localization.Get(
                        "StatisticsRemove"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
                return;

            HideWallpaperPreview();

            if (thumbnailCache.Remove(
                row.Path,
                out Image? thumbnail))
            {
                thumbnail.Dispose();
            }

            removeFromStatistics(row.Path);
            RefreshStatistics();
        }

        private void ResetButton_Click(
            object? sender,
            EventArgs e)
        {
            if (resetStatistics == null)
                return;

            string message =
                Localization.Get(
                    "StatisticsResetConfirm");

            DialogResult result =
                MessageBox.Show(
                    this,
                    message,
                    Localization.Get(
                        "StatisticsResetTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
                return;

            HideWallpaperPreview();
            resetStatistics();

            statisticsStartedAt =
                DateTime.Now;

            dailyStatisticsStartedAt =
                statisticsStartedAt;

            foreach (Image image in
                thumbnailCache.Values)
            {
                image.Dispose();
            }

            thumbnailCache.Clear();
            thumbnailLoading.Clear();

            RefreshStatistics();
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

            return $"{bytes} B";
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

                if (control is TextBox textBox)
                {
                    textBox.BackColor =
                        inputBackground;
                }
            }

            Color cardBackground =
                darkMode
                    ? Color.FromArgb(38, 38, 38)
                    : Color.White;

            Color cardMuted =
                darkMode
                    ? Color.FromArgb(170, 170, 170)
                    : Color.FromArgb(95, 95, 95);

            mostViewedCard.SetColors(
                cardBackground,
                foreground,
                cardMuted);

            leastViewedCard.SetColors(
                cardBackground,
                foreground,
                cardMuted);

            averageCard.SetColors(
                cardBackground,
                foreground,
                cardMuted);

            fairnessCard.SetColors(
                cardBackground,
                foreground,
                cardMuted);

            statisticsList.BackColor =
                darkMode
                    ? Color.FromArgb(
                        34, 34, 34)
                    : Color.White;

            statisticsList.ForeColor =
                foreground;

            rowContextMenu.BackColor =
                darkMode
                    ? Color.FromArgb(
                        40, 40, 40)
                    : SystemColors.Control;

            rowContextMenu.ForeColor =
                foreground;

            removeStatisticsMenuItem.ForeColor =
                darkMode
                    ? Color.FromArgb(235, 150, 150)
                    : Color.FromArgb(165, 45, 45);

            wallpaperPreviewForm.BackColor =
                darkMode
                    ? Color.FromArgb(
                        28, 28, 28)
                    : Color.White;

            wallpaperPreviewForm.ForeColor =
                foreground;

            wallpaperPreviewPictureBox.BackColor =
                Color.Black;

            wallpaperPreviewInfoLabel.ForeColor =
                foreground;

            resetButton.UseVisualStyleBackColor =
                false;

            resetButton.BackColor =
                darkMode
                    ? Color.FromArgb(
                        50, 50, 50)
                    : SystemColors.Control;

            resetButton.ForeColor =
                foreground;

            resetButton.FlatStyle =
                FlatStyle.Flat;

            resetButton.FlatAppearance.BorderColor =
                darkMode
                    ? Color.FromArgb(
                        85, 85, 85)
                    : Color.FromArgb(
                        180, 180, 180);

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
