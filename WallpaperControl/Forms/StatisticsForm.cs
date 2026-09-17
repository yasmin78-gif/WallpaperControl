using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private readonly List<Font> ownedFonts = new();
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_HIDE = 0;

        private readonly IReadOnlyDictionary<string, int> wallpaperViewCounts;
        private readonly IReadOnlyDictionary<string, DateTime> wallpaperLastShown;
        private readonly IReadOnlyDictionary<string, Dictionary<string, int>>
            wallpaperDailyViewCounts;
        private readonly IReadOnlyDictionary<string, int>
            wallpaperRecurrenceCounts;
        private readonly IReadOnlyDictionary<string, double>
            wallpaperRecurrenceSeconds;
        private readonly string wallpaperFolder;
        private DateTime statisticsStartedAt;
        private DateTime dailyStatisticsStartedAt;
        private DateTime recurrenceStatisticsStartedAt;
        private readonly Action<string>? removeFromStatistics;
        private readonly Action<string>? setWallpaper;
        private readonly Action? resetStatistics;

        private readonly Label summaryLabel;
        private readonly MetricCard mostViewedCard;
        private readonly MetricCard leastViewedCard;
        private readonly MetricCard averageCard;
        private readonly MetricCard fairnessCard;
        private readonly TopWallpaperChart topWallpaperChart;
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
        private SortColumn sortColumnBeforeNeglected = SortColumn.Views;
        private bool sortAscendingBeforeNeglected = false;
        private bool neglectedModeActive = false;

        private enum SortColumn
        {
            Wallpaper,
            Views,
            Share,
            LastShown
        }

        private sealed class TopWallpaperChart : Panel
        {
            private IReadOnlyList<(string Name, int Views)> data =
                Array.Empty<(string Name, int Views)>();

            private bool darkMode;
            private string emptyText = string.Empty;

            [DesignerSerializationVisibility(
                DesignerSerializationVisibility.Hidden)]
            public string Title { get; set; } = string.Empty;

            /// <summary>
            /// Configures flicker-free rendering for the top-wallpaper chart.
            /// </summary>
            public TopWallpaperChart()
            {
                DoubleBuffered = true;
                BorderStyle = BorderStyle.FixedSingle;
            }

            /// <summary>
            /// Replaces the chart entries and its empty-state caption, then schedules a repaint.
            /// </summary>
            /// <param name="values">The wallpaper names and counts to plot.</param>
            /// <param name="noDataText">The localized caption shown when there are no chart values.</param>
            public void SetData(
                IEnumerable<KeyValuePair<string, int>> values,
                string noDataText)
            {
                data = values
                    .Where(item => item.Value > 0)
                    .OrderByDescending(item => item.Value)
                    .ThenBy(
                        item => Path.GetFileName(item.Key),
                        StringComparer.CurrentCultureIgnoreCase)
                    .Take(10)
                    .Select(item =>
                        (Path.GetFileName(item.Key), item.Value))
                    .ToList();

                emptyText = noDataText;
                Invalidate();
            }

            /// <summary>
            /// Updates the chart palette and schedules a repaint.
            /// </summary>
            /// <param name="useDarkMode">True to select the dark palette.</param>
            public void SetTheme(bool useDarkMode)
            {
                darkMode = useDarkMode;
                BackColor = AppTheme.PanelBackground(darkMode);
                ForeColor = AppTheme.TextPrimary(darkMode);
                Invalidate();
            }

            /// <summary>
            /// Draws the ranked wallpaper bars or the localized empty-state message.
            /// </summary>
            /// <param name="e">The event data supplied by WinForms or the event source.</param>
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using Font titleFont = new Font(
                    "Segoe UI", 9.5f, FontStyle.Bold);
                using Font rowFont = new Font(
                    "Segoe UI", 8.5f, FontStyle.Regular);
                using Font valueFont = new Font(
                    "Segoe UI", 8.5f, FontStyle.Bold);

                Color muted = darkMode
                    ? AppTheme.DarkTextSecondary
                    : Color.FromArgb(95, 95, 95);
                Color track = darkMode
                    ? AppTheme.DarkControl
                    : Color.FromArgb(230, 230, 230);
                Color bar = darkMode
                    ? AppTheme.DarkAccentSoft
                    : Color.FromArgb(75, 125, 180);

                TextRenderer.DrawText(
                    g, Title, titleFont,
                    new Rectangle(12, 8, Width - 24, 22),
                    ForeColor,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);

                if (data.Count == 0)
                {
                    TextRenderer.DrawText(
                        g, emptyText, rowFont,
                        new Rectangle(12, 40, Width - 24, Height - 48),
                        muted,
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis);
                    return;
                }

                int maxViews = Math.Max(1, data.Max(item => item.Views));
                int rows = data.Count;
                int availableHeight = Height - 38;
                int rowHeight = Math.Max(12, availableHeight / rows);
                int labelWidth = Math.Min(255, Math.Max(170, Width / 3));
                int valueWidth = 55;
                int barLeft = labelWidth + 20;
                int barRight = Width - valueWidth - 18;
                int barWidth = Math.Max(40, barRight - barLeft);

                for (int i = 0; i < rows; i++)
                {
                    var item = data[i];
                    int y = 35 + i * rowHeight;
                    int h = Math.Max(7, rowHeight - 5);

                    TextRenderer.DrawText(
                        g, item.Name, rowFont,
                        new Rectangle(12, y - 2, labelWidth, rowHeight),
                        ForeColor,
                        TextFormatFlags.Left |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix);

                    Rectangle trackRect = new Rectangle(
                        barLeft, y + 2, barWidth, h);
                    using (SolidBrush trackBrush = new(track))
                        g.FillRectangle(trackBrush, trackRect);

                    int filled = Math.Max(2,
                        (int)Math.Round(barWidth *
                        item.Views / (double)maxViews));
                    Rectangle barRect = new Rectangle(
                        barLeft, y + 2, Math.Min(barWidth, filled), h);
                    using (SolidBrush barBrush = new(bar))
                        g.FillRectangle(barBrush, barRect);

                    TextRenderer.DrawText(
                        g, item.Views.ToString(
                            "N0", Localization.CurrentCulture),
                        valueFont,
                        new Rectangle(barRight + 5, y - 2, valueWidth, rowHeight),
                        ForeColor,
                        TextFormatFlags.Right |
                        TextFormatFlags.VerticalCenter);
                }
            }
        }

        private sealed class MetricCard : Panel
        {
            private readonly Font titleFont;
            private readonly Font valueFont;
            private readonly Font detailFont;

            public Label TitleLabel { get; }
            public Label ValueLabel { get; }
            public Label DetailLabel { get; }

            /// <summary>
            /// Builds the value and detail labels used by a statistics dashboard card.
            /// </summary>
            public MetricCard()
            {
                Size = new Size(205, 88);
                Padding = new Padding(12);
                BorderStyle = BorderStyle.FixedSingle;

                titleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
                valueFont = new Font("Segoe UI", 15, FontStyle.Bold);
                detailFont = new Font("Segoe UI", 8, FontStyle.Regular);

                TitleLabel = new Label
                {
                    Location = new Point(12, 9),
                    Size = new Size(179, 20),
                    Font = titleFont,
                    AutoEllipsis = true
                };

                ValueLabel = new Label
                {
                    Location = new Point(12, 30),
                    Size = new Size(179, 28),
                    Font = valueFont,
                    AutoEllipsis = true
                };

                DetailLabel = new Label
                {
                    Location = new Point(12, 60),
                    Size = new Size(179, 18),
                    Font = detailFont,
                    AutoEllipsis = true
                };

                Controls.Add(TitleLabel);
                Controls.Add(ValueLabel);
                Controls.Add(DetailLabel);
            }

            /// <summary>
            /// Releases the resources owned by this metric card.
            /// </summary>
            /// <param name="disposing">True when managed resources should be released during explicit disposal.</param>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    titleFont.Dispose();
                    valueFont.Dispose();
                    detailFont.Dispose();
                }

                base.Dispose(disposing);
            }

            /// <summary>
            /// Applies the dashboard card&apos;s background, value, and detail colors.
            /// </summary>
            /// <param name="background">The background color to apply.</param>
            /// <param name="foreground">The primary foreground color to apply.</param>
            /// <param name="muted">The secondary text color.</param>
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

            /// <summary>
            /// Pairs a statistics period with the label displayed in the period selector.
            /// </summary>
            /// <param name="text">The text to display, format, or parse.</param>
            /// <param name="period">The statistics period to display or aggregate.</param>
            public PeriodChoice(
                string text,
                StatisticsPeriod period)
            {
                Text = text;
                Period = period;
            }

            /// <summary>
            /// Returns the localized display label for this selection item.
            /// </summary>
            /// <returns>The display label shown in the selection control.</returns>
            public override string ToString() => Text;
        }

        private sealed class FilterChoice
        {
            public string Text { get; }
            public int? MaxItems { get; }
            public bool NeglectedOnly { get; }

            /// <summary>
            /// Stores the filter label, optional result limit, and neglected-wallpaper mode.
            /// </summary>
            /// <param name="text">The text to display, format, or parse.</param>
            /// <param name="maxItems">The optional maximum number of rows to show.</param>
            /// <param name="neglectedOnly">Whether to limit results to wallpapers with low or missing view counts.</param>
            public FilterChoice(
                string text,
                int? maxItems,
                bool neglectedOnly = false)
            {
                Text = text;
                MaxItems = maxItems;
                NeglectedOnly = neglectedOnly;
            }

            /// <summary>
            /// Returns the localized display label for this selection item.
            /// </summary>
            /// <returns>The display label shown in the selection control.</returns>
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

            /// <summary>
            /// Captures one wallpaper&apos;s path, count, last display time, share, and popularity rank.
            /// </summary>
            /// <param name="path">The image or folder path to process.</param>
            /// <param name="views">The number of views recorded for the wallpaper.</param>
            /// <param name="lastShown">The most recent display timestamp for this wallpaper.</param>
            /// <param name="share">The wallpaper&apos;s share of the selected display counts.</param>
            /// <param name="popularityRank">The wallpaper&apos;s position in the popularity ordering.</param>
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
            /// <summary>
            /// Enables double buffering to reduce flicker in the statistics list.
            /// </summary>
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

            /// <summary>
            /// Stores the theme used to paint the statistics context menu.
            /// </summary>
            /// <param name="dark">True to use dark menu colors.</param>
            public StatisticsMenuColorTable(
                bool dark)
            {
                this.dark = dark;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground =>
                dark
                    ? AppTheme.DarkPanel
                    : Color.White;

            public override Color ImageMarginGradientBegin =>
                ToolStripDropDownBackground;

            public override Color ImageMarginGradientMiddle =>
                ToolStripDropDownBackground;

            public override Color ImageMarginGradientEnd =>
                ToolStripDropDownBackground;

            public override Color MenuItemSelected =>
                dark
                    ? AppTheme.DarkControlHover
                    : Color.FromArgb(232, 240, 248);

            public override Color MenuItemBorder =>
                dark
                    ? AppTheme.DarkBorder
                    : Color.FromArgb(190, 205, 220);

            public override Color MenuBorder =>
                dark
                    ? AppTheme.DarkBorderStrong
                    : Color.FromArgb(175, 175, 175);

            public override Color SeparatorDark =>
                dark
                    ? AppTheme.DarkBorder
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

        /// <summary>
        /// Builds the statistics dashboard from the supplied counters and connects optional wallpaper actions.
        /// </summary>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <param name="windowOpacityPercent">The window opacity as a percentage.</param>
        /// <param name="wallpaperViewCounts">The total display count for each wallpaper.</param>
        /// <param name="wallpaperLastShown">The last display time recorded for each wallpaper.</param>
        /// <param name="wallpaperDailyViewCounts">The per-day display counts for each wallpaper.</param>
        /// <param name="wallpaperRecurrenceCounts">The number of recorded repeat appearances for each wallpaper.</param>
        /// <param name="wallpaperRecurrenceSeconds">The accumulated intervals between repeat appearances, in seconds.</param>
        /// <param name="wallpaperFolder">The current source folder used to find wallpaper images.</param>
        /// <param name="statisticsStartedAt">The start of the overall statistics period.</param>
        /// <param name="dailyStatisticsStartedAt">The start of per-day statistics.</param>
        /// <param name="recurrenceStatisticsStartedAt">The start of recurrence statistics.</param>
        /// <param name="removeFromStatistics">The optional callback that removes one image&apos;s tracking data.</param>
        /// <param name="setWallpaper">The optional callback that displays a selected wallpaper.</param>
        /// <param name="resetStatistics">The optional callback that clears tracking data.</param>
        public StatisticsForm(
            bool darkMode,
            int windowOpacityPercent,
            IReadOnlyDictionary<string, int> wallpaperViewCounts,
            IReadOnlyDictionary<string, DateTime> wallpaperLastShown,
            IReadOnlyDictionary<string, Dictionary<string, int>>
                wallpaperDailyViewCounts,
            IReadOnlyDictionary<string, int>
                wallpaperRecurrenceCounts,
            IReadOnlyDictionary<string, double>
                wallpaperRecurrenceSeconds,
            string wallpaperFolder,
            DateTime statisticsStartedAt,
            DateTime dailyStatisticsStartedAt,
            DateTime recurrenceStatisticsStartedAt,
            Action<string>? removeFromStatistics = null,
            Action<string>? setWallpaper = null,
            Action? resetStatistics = null)
        {
            this.darkMode = darkMode;
            this.wallpaperViewCounts = wallpaperViewCounts;
            this.wallpaperLastShown = wallpaperLastShown;
            this.wallpaperDailyViewCounts = wallpaperDailyViewCounts;
            this.wallpaperRecurrenceCounts = wallpaperRecurrenceCounts;
            this.wallpaperRecurrenceSeconds = wallpaperRecurrenceSeconds;
            this.wallpaperFolder = wallpaperFolder ?? string.Empty;
            this.statisticsStartedAt = statisticsStartedAt;
            this.dailyStatisticsStartedAt = dailyStatisticsStartedAt;
            this.recurrenceStatisticsStartedAt = recurrenceStatisticsStartedAt;
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

            ClientSize = new Size(920, 900);
            Font = CreateOwnedFont("Segoe UI", 10);

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
                Font = CreateOwnedFont(
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

            topWallpaperChart = new TopWallpaperChart
            {
                Location = new Point(25, 190),
                Size = new Size(870, 175),
                Title = Localization.Get(
                    "StatisticsChartTop10")
            };

            Label filterLabel = new Label
            {
                Text = Localization.Get(
                    "StatisticsFilterLabel"),
                Location = new Point(25, 389),
                Size = new Size(105, 25)
            };

            filterComboBox = new ComboBox
            {
                Location = new Point(130, 384),
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

            filterComboBox.Items.Add(
                new FilterChoice(
                    Localization.Get(
                        "StatisticsFilterNeglected"),
                    null,
                    neglectedOnly: true));

            filterComboBox.SelectedIndex = 0;

            filterComboBox.SelectedIndexChanged +=
                FilterComboBox_SelectedIndexChanged;

            Label periodLabel = new Label
            {
                Text = Localization.Get(
                    "StatisticsPeriodLabel"),
                Location = new Point(300, 389),
                Size = new Size(80, 25)
            };

            periodComboBox = new ComboBox
            {
                Location = new Point(380, 384),
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
                Location = new Point(590, 384),
                Size = new Size(305, 27),
                PlaceholderText =
                    Localization.Get(
                        "StatisticsSearchPlaceholder")
            };

            searchTextBox.TextChanged +=
                (_, _) => RefreshStatistics();

            statisticsList = new DoubleBufferedListView
            {
                Location = new Point(25, 428),
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
                Font = CreateOwnedFont(
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
                CreateOwnedFont(
                    rowContextMenu.Font,
                    FontStyle.Bold);

            removeStatisticsMenuItem.ForeColor =
                darkMode
                    ? AppTheme.DarkDanger
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
                Location = new Point(25, 835),
                Size = new Size(190, 34)
            };

            resetButton.Click +=
                ResetButton_Click;

            closeButton = new Button
            {
                Text = Localization.Get("AboutClose"),
                Location = new Point(770, 835),
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
            Controls.Add(topWallpaperChart);
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

        /// <summary>
        /// Creates a font and tracks it for disposal with the form.
        /// </summary>
        /// <param name="familyName">The name of the font family to create.</param>
        /// <param name="emSize">The font size in the units used by the drawing operation.</param>
        /// <param name="style">The weight and decoration applied to the font.</param>
        /// <returns>The font owned by the form; it is released when the form is disposed.</returns>
        private Font CreateOwnedFont(string familyName, float emSize, FontStyle style = FontStyle.Regular)
        {
            Font font = new Font(familyName, emSize, style);
            ownedFonts.Add(font);
            return font;
        }

        /// <summary>
        /// Creates a font and tracks it for disposal with the form.
        /// </summary>
        /// <param name="prototype">The existing font whose family and size are reused.</param>
        /// <param name="style">The weight and decoration applied to the font.</param>
        /// <returns>The font owned by the form; it is released when the form is disposed.</returns>
        private Font CreateOwnedFont(Font prototype, FontStyle style)
        {
            Font font = new Font(prototype, style);
            ownedFonts.Add(font);
            return font;
        }

        /// <summary>
        /// Applies the native title-bar theme after the window handle is created.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        protected override void OnHandleCreated(
            EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBarTheme();
        }

        /// <summary>
        /// Cancels pending thumbnail work and releases preview, menu, image, and font resources.
        /// </summary>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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

            foreach (Font font in ownedFonts)
            {
                font.Dispose();
            }
            ownedFonts.Clear();

            base.OnFormClosed(e);
        }

        /// <summary>
        /// Refreshes statistics after the user selects a different result filter.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void FilterComboBox_SelectedIndexChanged(
            object? sender,
            EventArgs e)
        {
            bool neglected =
                filterComboBox.SelectedItem
                    is FilterChoice choice &&
                choice.NeglectedOnly;

            if (neglected &&
                !neglectedModeActive)
            {
                neglectedModeActive = true;
                sortColumnBeforeNeglected = sortColumn;
                sortAscendingBeforeNeglected = sortAscending;

                sortColumn = SortColumn.LastShown;
                sortAscending = true;

                if (periodComboBox.SelectedIndex != 0)
                {
                    periodComboBox.SelectedIndex = 0;
                }

                periodComboBox.Enabled = false;
                UpdateColumnHeaders();
            }
            else if (!neglected &&
                     neglectedModeActive)
            {
                neglectedModeActive = false;
                sortColumn = sortColumnBeforeNeglected;
                sortAscending = sortAscendingBeforeNeglected;
                periodComboBox.Enabled = true;
                UpdateColumnHeaders();
            }

            RefreshStatistics();
        }

        /// <summary>
        /// Rebuilds filtered statistics rows, totals, and dashboard values from the current tracking data.
        /// </summary>
        private void RefreshStatistics()
        {
            PeriodChoice periodChoice =
                periodComboBox.SelectedItem
                    as PeriodChoice ??
                new PeriodChoice(
                    Localization.Get(
                        "StatisticsPeriodAll"),
                    StatisticsPeriod.All);

            bool neglectedMode =
                filterComboBox.SelectedItem
                    is FilterChoice selectedFilter &&
                selectedFilter.NeglectedOnly;

            if (neglectedMode &&
                periodChoice.Period != StatisticsPeriod.All)
            {
                periodComboBox.SelectedIndex = 0;
                return;
            }

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

            int recurrenceEvents =
                wallpaperRecurrenceCounts.Values.Sum();

            double recurrenceTotalSeconds =
                wallpaperRecurrenceSeconds.Values.Sum();

            string recurrenceText =
                recurrenceEvents > 0 &&
                recurrenceTotalSeconds > 0
                    ? FormatRecurrenceDuration(
                        TimeSpan.FromSeconds(
                            recurrenceTotalSeconds /
                            recurrenceEvents))
                    : Localization.Get(
                        "StatisticsRecurrenceNoData");

            summaryLabel.Text +=
                "   •   " +
                string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsRecurrenceSummary"),
                    recurrenceText);

            dashboardToolTip.SetToolTip(
                summaryLabel,
                string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsRecurrenceToolTip"),
                    recurrenceStatisticsStartedAt,
                    recurrenceEvents));

            UpdateDashboardCards(
                activeViewCounts,
                totalViews,
                average);

            topWallpaperChart.Title =
                string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsChartTop10ForPeriod"),
                    periodChoice.Text);

            topWallpaperChart.SetData(
                activeViewCounts,
                Localization.Get(
                    "StatisticsMetricNoData"));

            List<RowData> popularityOrder;

            if (neglectedMode)
            {
                int allTimeTotalViews =
                    wallpaperViewCounts.Values.Sum();

                popularityOrder =
                    GetCurrentFolderWallpapers()
                        .Select(
                            path =>
                            {
                                wallpaperViewCounts.TryGetValue(
                                    path,
                                    out int views);

                                wallpaperLastShown.TryGetValue(
                                    path,
                                    out DateTime lastShown);

                                double share =
                                    allTimeTotalViews <= 0
                                        ? 0
                                        : views * 100.0 /
                                          allTimeTotalViews;

                                return new
                                {
                                    Path = path,
                                    Views = views,
                                    LastShown = lastShown,
                                    Share = share
                                };
                            })
                        .OrderBy(
                            item =>
                                item.LastShown == default
                                    ? DateTime.MinValue
                                    : item.LastShown)
                        .ThenBy(
                            item =>
                                Path.GetFileName(
                                    item.Path),
                            StringComparer
                                .CurrentCultureIgnoreCase)
                        .Select(
                            (item, index) =>
                                new RowData(
                                    item.Path,
                                    item.Views,
                                    item.LastShown,
                                    item.Share,
                                    index + 1))
                        .ToList();
            }
            else
            {
                popularityOrder =
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
            }

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

            if (!neglectedMode &&
                filterComboBox.SelectedItem
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
                            ? neglectedMode
                                ? Localization.Get(
                                    "StatisticsNeverShown")
                                : ""
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

        /// <summary>
        /// Collects supported images from the current wallpaper folder for statistics comparisons.
        /// </summary>
        /// <returns>The supported wallpaper paths discovered in the source folder.</returns>
        private List<string> GetCurrentFolderWallpapers()
        {
            if (string.IsNullOrWhiteSpace(
                    wallpaperFolder) ||
                !Directory.Exists(
                    wallpaperFolder))
            {
                return new List<string>();
            }

            try
            {
                return Directory
                    .EnumerateFiles(
                        wallpaperFolder,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Where(
                        WallpaperImageInfo.IsSupportedWallpaperExtension)
                    .OrderBy(
                        path =>
                            Path.GetFileName(path),
                        StringComparer
                            .CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Aggregates wallpaper view counts for the selected tracking period.
        /// </summary>
        /// <param name="period">The statistics period to display or aggregate.</param>
        /// <returns>The per-wallpaper counts for the selected period.</returns>
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

        /// <summary>
        /// Formats the elapsed time between repeated wallpaper displays for the statistics interface.
        /// </summary>
        /// <param name="duration">The average interval between wallpaper appearances.</param>
        /// <returns>The recurrence duration formatted for display.</returns>
        private static string FormatRecurrenceDuration(
            TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
            {
                return string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsRecurrenceDays"),
                    duration.TotalDays);
            }

            if (duration.TotalHours >= 1)
            {
                return string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsRecurrenceHours"),
                    duration.TotalHours);
            }

            if (duration.TotalMinutes >= 1)
            {
                return string.Format(
                    Localization.CurrentCulture,
                    Localization.Get(
                        "StatisticsRecurrenceMinutes"),
                    duration.TotalMinutes);
            }

            return string.Format(
                Localization.CurrentCulture,
                Localization.Get(
                    "StatisticsRecurrenceSeconds"),
                Math.Max(
                    0,
                    duration.TotalSeconds));
        }

        /// <summary>
        /// Refreshes the dashboard metrics for the current counts and selected period.
        /// </summary>
        /// <param name="activeViewCounts">The display counts for the currently selected period.</param>
        /// <param name="totalViews">The total number of recorded views in the selected data.</param>
        /// <param name="average">The average number of views per wallpaper in the selected data.</param>
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

        /// <summary>
        /// Sets the main value and explanatory detail on a dashboard card.
        /// </summary>
        /// <param name="card">The dashboard metric card to update.</param>
        /// <param name="value">The formatted value to display.</param>
        /// <param name="detail">The explanatory caption shown below the metric value.</param>
        private static void SetMetricCard(
            MetricCard card,
            string value,
            string detail)
        {
            card.ValueLabel.Text = value;
            card.DetailLabel.Text = detail;
        }

        /// <summary>
        /// Calculates how evenly wallpaper views are distributed across the supplied counts.
        /// </summary>
        /// <param name="viewCounts">The wallpaper display counts used by the calculation or snapshot.</param>
        /// <param name="totalViews">The total number of recorded views in the selected data.</param>
        /// <returns>The calculated percentage score describing the distribution of views.</returns>
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

        /// <summary>
        /// Orders statistics rows by the selected column and direction.
        /// </summary>
        /// <param name="items">The filtered statistics rows to order for display.</param>
        /// <returns>The rows ordered by the active sort selection.</returns>
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

        /// <summary>
        /// Changes the statistics sort column or toggles its direction, then refreshes the list.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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

        /// <summary>
        /// Refreshes localized column labels and marks the active sort direction.
        /// </summary>
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

        /// <summary>
        /// Adds the current sort indicator to the selected column&apos;s caption.
        /// </summary>
        /// <param name="text">The text to display, format, or parse.</param>
        /// <param name="column">The statistics column being sorted or labeled.</param>
        /// <returns>The caption with an indicator when this is the active sort column.</returns>
        private string BuildSortableHeader(
            string text,
            SortColumn column)
        {
            if (sortColumn != column)
                return text;

            return text +
                (sortAscending ? "  ▲" : "  ▼");
        }

        /// <summary>
        /// Draws a statistics column header using the active theme.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void StatisticsList_DrawColumnHeader(
            object? sender,
            DrawListViewColumnHeaderEventArgs e)
        {
            if (e.Header == null)
                return;

            Color backColor =
                darkMode
                    ? AppTheme.DarkControl
                    : Color.FromArgb(240, 240, 240);

            Color foreColor =
                AppTheme.TextPrimary(darkMode);

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
                        ? AppTheme.DarkBorder
                        : Color.FromArgb(
                            210, 210, 210));

            e.Graphics.DrawLine(
                linePen,
                e.Bounds.Left,
                e.Bounds.Bottom - 1,
                e.Bounds.Right,
                e.Bounds.Bottom - 1);
        }

        /// <summary>
        /// Draws a statistics cell, including its selection state and column-specific content.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
        private void StatisticsList_DrawSubItem(
            object? sender,
            DrawListViewSubItemEventArgs e)
        {
            if (e.Item == null ||
                e.SubItem == null)
            {
                return;
            }

            bool isHover =
                e.ItemIndex == hoveredItemIndex;

            bool alternate =
                e.ItemIndex % 2 == 1;

            Color background =
                darkMode
                    ? alternate
                        ? AppTheme.ListAlternateBackground(true)
                        : AppTheme.ListBackground(true)
                    : alternate
                        ? Color.FromArgb(248, 248, 248)
                        : Color.White;

            if (isHover)
            {
                background =
                    AppTheme.ListHoverBackground(darkMode);
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
                        ? AppTheme.DarkTextSecondary
                        : Color.Gray
                    : AppTheme.TextPrimary(darkMode);

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
                        ? AppTheme.DarkBorder
                        : Color.FromArgb(
                            235, 235, 235));

            e.Graphics.DrawLine(
                separatorPen,
                e.Bounds.Left,
                e.Bounds.Bottom - 1,
                e.Bounds.Right,
                e.Bounds.Bottom - 1);
        }

        /// <summary>
        /// Draws a cached image thumbnail or a placeholder within the supplied cell bounds.
        /// </summary>
        /// <param name="graphics">The drawing surface used for the operation.</param>
        /// <param name="bounds">The rectangle used for drawing or visibility checks.</param>
        /// <param name="row">The wallpaper metadata associated with the row, when available.</param>
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
                        ? AppTheme.DarkSidebar
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
                            ? AppTheme.DarkBorderStrong
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
                                ? AppTheme.DarkTextSecondary
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
                        ? AppTheme.DarkBorder
                        : Color.FromArgb(
                            180, 180, 180));

            graphics.DrawRectangle(
                imageBorder,
                imageBounds);
        }

        /// <summary>
        /// Queues uncached thumbnail work while avoiding duplicate requests and respecting shutdown.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
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

        /// <summary>
        /// Loads and crops a wallpaper into the fixed-size thumbnail used by the statistics list.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        /// <returns>The owned thumbnail bitmap, or null when the source cannot be loaded.</returns>
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

        /// <summary>
        /// Calculates the source crop that fills the target thumbnail while preserving the aspect ratio.
        /// </summary>
        /// <param name="sourceWidth">The source image width in pixels.</param>
        /// <param name="sourceHeight">The source image height in pixels.</param>
        /// <param name="targetWidth">The target image width in pixels.</param>
        /// <param name="targetHeight">The target image height in pixels.</param>
        /// <returns>The source rectangle that fills the target aspect ratio.</returns>
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

        /// <summary>
        /// Updates hovered-row state and the wallpaper preview as the pointer moves.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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

        /// <summary>
        /// Clears hover state and hides the wallpaper preview when the pointer leaves the list.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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

        /// <summary>
        /// Selects the row under the pointer before processing its context-menu actions.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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

        /// <summary>
        /// Opens the selected wallpaper from a list double-click.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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

        /// <summary>
        /// Retrieves the wallpaper data associated with the currently selected list row.
        /// </summary>
        /// <returns>The selected wallpaper data, or null when no suitable row is selected.</returns>
        private RowData? GetSelectedRow()
        {
            if (statisticsList.SelectedItems.Count == 0)
                return null;

            return statisticsList
                .SelectedItems[0]
                .Tag as RowData;
        }

        /// <summary>
        /// Requests the selected image as the desktop wallpaper through the supplied callback.
        /// </summary>
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

        /// <summary>
        /// Opens the selected wallpaper with its registered default application.
        /// </summary>
        private void OpenSelectedWallpaper()
        {
            RowData? row =
                GetSelectedRow();

            if (row?.Exists == true)
            {
                OpenWallpaper(row.Path);
            }
        }

        /// <summary>
        /// Opens an existing image and reports failures through the statistics dialog.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
        private static void OpenWallpaper(
            string path)
        {
            try
            {
                WallpaperFileActions.OpenImage(path);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Reveals the selected wallpaper in Windows Explorer.
        /// </summary>
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
                WallpaperFileActions.RevealInExplorer(row.Path);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Copies the selected image&apos;s path to the clipboard.
        /// </summary>
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

        /// <summary>
        /// Removes the selected wallpaper&apos;s tracking data and refreshes the dashboard.
        /// </summary>
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

        /// <summary>
        /// Confirms and requests a statistics reset, then refreshes the displayed data.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event data supplied by WinForms or the event source.</param>
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

        /// <summary>
        /// Loads and positions a preview with metadata for the selected wallpaper.
        /// </summary>
        /// <param name="path">The image or folder path to process.</param>
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
                    WallpaperImageInfo.GetImageResolutionText(path);

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

        /// <summary>
        /// Hides the statistics preview window without activating another window.
        /// </summary>
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

        /// <summary>
        /// Formats a file size using the statistics dialog&apos;s compact unit labels.
        /// </summary>
        /// <param name="bytes">The size in bytes.</param>
        /// <returns>The size formatted with compact byte, kilobyte, megabyte, or gigabyte units.</returns>
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

        /// <summary>
        /// Applies the selected palette to the statistics dashboard and its controls.
        /// </summary>
        private void ApplyTheme()
        {
            Color background =
                AppTheme.WindowBackground(darkMode);

            Color foreground =
                AppTheme.TextPrimary(darkMode);

            Color inputBackground =
                AppTheme.InputBackground(darkMode);

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
                AppTheme.PanelBackground(darkMode);

            Color cardMuted =
                darkMode
                    ? AppTheme.DarkTextSecondary
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

            topWallpaperChart.SetTheme(
                darkMode);

            statisticsList.BackColor =
                AppTheme.ListBackground(darkMode);

            statisticsList.ForeColor =
                foreground;

            rowContextMenu.BackColor =
                AppTheme.MenuBackground(darkMode);

            rowContextMenu.ForeColor =
                foreground;

            removeStatisticsMenuItem.ForeColor =
                darkMode
                    ? AppTheme.DarkDanger
                    : Color.FromArgb(165, 45, 45);

            wallpaperPreviewForm.BackColor =
                AppTheme.PanelBackground(darkMode);

            wallpaperPreviewForm.ForeColor =
                foreground;

            wallpaperPreviewPictureBox.BackColor =
                Color.Black;

            wallpaperPreviewInfoLabel.ForeColor =
                foreground;

            resetButton.UseVisualStyleBackColor =
                false;

            resetButton.BackColor =
                AppTheme.ControlBackground(darkMode);

            resetButton.ForeColor =
                foreground;

            resetButton.FlatStyle =
                FlatStyle.Flat;

            resetButton.FlatAppearance.BorderColor =
                AppTheme.Border(darkMode);
            resetButton.FlatAppearance.MouseOverBackColor =
                AppTheme.ControlHover(darkMode);
            resetButton.FlatAppearance.MouseDownBackColor =
                AppTheme.ControlPressed(darkMode);

            closeButton.UseVisualStyleBackColor =
                false;

            closeButton.BackColor =
                AppTheme.ControlBackground(darkMode);

            closeButton.ForeColor =
                foreground;

            closeButton.FlatStyle =
                FlatStyle.Flat;

            closeButton.FlatAppearance.BorderColor =
                AppTheme.Border(darkMode);
            closeButton.FlatAppearance.MouseOverBackColor =
                AppTheme.ControlHover(darkMode);
            closeButton.FlatAppearance.MouseDownBackColor =
                AppTheme.ControlPressed(darkMode);

            ApplyTitleBarTheme();
        }

        /// <summary>
        /// Applies the current theme to the native statistics window title bar.
        /// </summary>
        private void ApplyTitleBarTheme()
        {
            WindowsTheme.ApplyTitleBar(this, darkMode);
        }

        /// <summary>
        /// Changes the visibility or display state of a native window.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="nCmdShow">The native command controlling visibility and restored or minimized state.</param>
        /// <returns>True if the window was previously visible; otherwise, false.</returns>
        [DllImport("user32.dll")]
        private static extern bool
            ShowWindow(
                IntPtr hWnd,
                int nCmdShow);
    }
}
