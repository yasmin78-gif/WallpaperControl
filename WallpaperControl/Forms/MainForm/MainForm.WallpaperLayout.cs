namespace WallpaperControl
{
    public partial class MainForm
    {
        private readonly Panel slideshowCard = new();
        private readonly Panel displayCard = new();
        private readonly Panel currentWallpaperCard = new();
        private readonly Label slideshowHeading = new();
        private readonly Label displayHeading = new();
        private bool wallpaperLayoutReady;
        private bool arrangingWallpaper;
        private bool wallpaperWarning;
        private bool wallpaperActivation;

        private void InitializeWallpaperPage()
        {
            folderTextBox.AutoSize = false;
            slideshowHeading.Font = displayHeading.Font = CreateOwnedFont("Segoe UI", 12, FontStyle.Bold);
            Font fieldFont = CreateOwnedFont("Segoe UI", 10);
            foreach (Label label in new[] { folderLabel, intervalLabel, positionLabel, transitionLabel, directionHeading, transitionDurationLabel })
                label.Font = fieldFont;
            slideshowCard.Controls.AddRange(new Control[] { slideshowHeading, folderLabel, folderTextBox, folderButton,
                wallpaperCountLabel, intervalLabel, intervalComboBox, windowsIntervalLabel, shuffleCheckBox });
            displayCard.Controls.AddRange(new Control[] { displayHeading, positionLabel, positionComboBox, transitionLabel,
                transitionComboBox, directionHeading, transitionDirectionComboBox, transitionDurationLabel, transitionDurationComboBox });
            currentWallpaperCard.Controls.AddRange(new Control[] { currentHeading, currentWallpaperLabel, pauseButton, pinButton,
                explorerButton, rejectButton, undoRejectButton, historyButton, statisticsButton });
            wallpaperContent!.Controls.AddRange(new Control[] { slideshowCard, displayCard, currentWallpaperCard });
            wallpaperContent.TabIndex = 0;
            mainHeading.TabIndex = 0; statusLabel.TabIndex = 1; activateButton.TabIndex = 2;
            slideshowCard.TabIndex = 3; displayCard.TabIndex = 4; nextWallpaperButton.TabIndex = 5; currentWallpaperCard.TabIndex = 6;
            int tab = 0;
            foreach (Control control in new Control[] { folderTextBox, folderButton, intervalComboBox, shuffleCheckBox,
                positionComboBox, transitionComboBox, transitionDirectionComboBox, transitionDurationComboBox,
                pauseButton, pinButton, explorerButton, rejectButton, undoRejectButton, historyButton, statisticsButton })
                control.TabIndex = tab++;
            wallpaperLayoutReady = true;
            LocalizeWallpaperGroups();
        }

        private void LocalizeWallpaperGroups()
        {
            mainHeading.Text = Localization.Get("MainNavWallpaper");
            slideshowHeading.Text = Localization.Get("MainSlideshowHeading");
            displayHeading.Text = Localization.Get("MainDisplayHeading");
            LayoutWallpaperPage();
        }

        private IEnumerable<Control> WallpaperControls()
        {
            if (wallpaperContent == null) yield break;
            foreach (Control control in wallpaperContent.Controls)
            {
                yield return control;
                foreach (Control child in control.Controls) yield return child;
            }
        }

        private void ApplyWallpaperPageTheme()
        {
            foreach (Panel card in new[] { slideshowCard, displayCard, currentWallpaperCard })
            {
                card.BackColor = AppTheme.PanelBackground(darkMode);
                card.ForeColor = AppTheme.TextPrimary(darkMode);
            }
            slideshowHeading.ForeColor = displayHeading.ForeColor = AppTheme.TextPrimary(darkMode);
            transitionLabel.ForeColor = transitionDurationLabel.ForeColor = AppTheme.TextPrimary(darkMode);
        }

        private void SetNormalLayout()
        {
            wallpaperWarning = wallpaperActivation = false;
            LayoutWallpaperPage();
        }

        private void SetWarningLayout(bool showActivateButton)
        {
            wallpaperWarning = true;
            wallpaperActivation = showActivateButton;
            LayoutWallpaperPage();
        }

        private void LayoutWallpaperPage() => ArrangeWallpaperPage(DeviceDpi / 96f);

        /// <summary>Uses measured text and shared spacing to arrange only the Wallpaper workspace.</summary>
        private void ArrangeWallpaperPage(float scale)
        {
            if (!wallpaperLayoutReady || arrangingWallpaper || Disposing || IsDisposed || wallpaperPage!.IsDisposed) return;
            arrangingWallpaper = true;
            int viewportWidth = wallpaperPage.ClientSize.Width;
            wallpaperPage.SuspendLayout();
            wallpaperContent!.SuspendLayout();
            try
            {
                int Px(int value) => (int)Math.Round(value * scale);
                int gap = Px(16), padding = Px(20), small = Px(8);
                int width = Math.Max(1, Math.Min(Px(880), wallpaperPage.ClientSize.Width - 2 * gap));
                bool columns = width >= Px(760);
                int cardWidth = columns ? (width - gap) / 2 : width;
                int fieldWidth = Math.Max(1, cardWidth - 2 * padding);
                int LabelAt(Label label, int x, int y, int availableWidth, int minimum = 22)
                {
                    label.AutoSize = false;
                    int height = Math.Max(Px(minimum), TextRenderer.MeasureText(label.Text, label.Font,
                        new Size(Math.Max(1, availableWidth), int.MaxValue), TextFormatFlags.WordBreak).Height);
                    label.SetBounds(x, y, availableWidth, height);
                    return label.Bottom;
                }
                int InputAt(Control input, int x, int y, int availableWidth)
                {
                    if (input is MainFormComboBox combo)
                    {
                        int itemHeight = Math.Max(combo.Font.Height + Px(8), Px(26));
                        if (combo.ItemHeight != itemHeight) combo.ItemHeight = itemHeight;
                        // ComboBox.SetBoundsCore closes the native popup even when
                        // the resulting bounds are unchanged. Its height follows ItemHeight.
                        if (combo.Left != x || combo.Top != y || combo.Width != availableWidth)
                            combo.SetBounds(x, y, availableWidth, combo.Height);
                        return combo.Bottom;
                    }
                    input.SetBounds(x, y, availableWidth, Math.Max(Px(30), input.PreferredSize.Height));
                    return input.Bottom;
                }
                int Field(Label caption, Control input, int x, int y, int availableWidth) =>
                    InputAt(input, x, LabelAt(caption, x, y, availableWidth) + Px(4), availableWidth) + small;

                int top = LabelAt(mainHeading, 0, 0, width, 32) + gap;
                if (wallpaperWarning)
                {
                    top = LabelAt(statusLabel, 0, top, width) + small;
                    if (wallpaperActivation)
                    {
                        activateButton.SetBounds(0, top, width, Px(36));
                        top = activateButton.Bottom + small;
                    }
                }

                int leftY = LabelAt(slideshowHeading, padding, padding, fieldWidth, 28) + gap;
                leftY = LabelAt(folderLabel, padding, leftY, fieldWidth) + Px(4);
                int browseWidth = Px(54);
                int folderBottom = InputAt(folderTextBox, padding, leftY, Math.Max(1, fieldWidth - browseWidth - small));
                folderButton.SetBounds(padding + fieldWidth - browseWidth, leftY, browseWidth, Math.Max(Px(30), folderTextBox.Height));
                leftY = LabelAt(wallpaperCountLabel, padding, Math.Max(folderBottom, folderButton.Bottom) + small, fieldWidth, 20) + gap;
                leftY = Field(intervalLabel, intervalComboBox, padding, leftY, fieldWidth);
                leftY = LabelAt(windowsIntervalLabel, padding, leftY, fieldWidth, 20) + small;
                shuffleCheckBox.AutoSize = false;
                int shuffleHeight = Math.Max(Px(26), TextRenderer.MeasureText(shuffleCheckBox.Text, shuffleCheckBox.Font,
                    new Size(Math.Max(1, fieldWidth - Px(24)), int.MaxValue), TextFormatFlags.WordBreak).Height + Px(4));
                shuffleCheckBox.SetBounds(padding, leftY, fieldWidth, shuffleHeight);
                int slideshowHeight = shuffleCheckBox.Bottom + padding;

                int rightY = LabelAt(displayHeading, padding, padding, fieldWidth, 28) + gap;
                rightY = Field(positionLabel, positionComboBox, padding, rightY, fieldWidth);
                rightY = Field(transitionLabel, transitionComboBox, padding, rightY, fieldWidth);
                int half = (fieldWidth - small) / 2;
                int directionBottom = LabelAt(directionHeading, padding, rightY, half);
                int durationBottom = LabelAt(transitionDurationLabel, padding + half + small, rightY, half);
                int selectorY = Math.Max(directionBottom, durationBottom) + Px(4);
                int displayHeight = Math.Max(InputAt(transitionDirectionComboBox, padding, selectorY, half),
                    InputAt(transitionDurationComboBox, padding + half + small, selectorY, half)) + padding;

                int sharedHeight = Math.Max(slideshowHeight, displayHeight);
                slideshowCard.SetBounds(0, top, cardWidth, columns ? sharedHeight : slideshowHeight);
                displayCard.SetBounds(columns ? cardWidth + gap : 0, columns ? top : slideshowCard.Bottom + gap,
                    columns ? width - cardWidth - gap : width, columns ? sharedHeight : displayHeight);
                nextWallpaperButton.SetBounds(0, displayCard.Bottom + gap, width, Px(44));

                int innerWidth = Math.Max(1, width - 2 * padding);
                int currentY = LabelAt(currentHeading, padding, padding, innerWidth, 24) + small;
                currentWallpaperLabel.AutoSize = false;
                currentWallpaperLabel.AutoEllipsis = true;
                currentWallpaperLabel.SetBounds(padding, currentY, innerWidth, Math.Max(Px(26), currentWallpaperLabel.Font.Height + Px(4)));
                currentY = currentWallpaperLabel.Bottom + gap;
                void ActionRow(params Button[] buttons)
                {
                    int buttonWidth = (innerWidth - small * (buttons.Length - 1)) / buttons.Length;
                    int buttonHeight = Math.Max(Px(36), buttons.Max(b => TextRenderer.MeasureText(b.Text, b.Font,
                        new Size(Math.Max(1, buttonWidth - Px(16)), int.MaxValue), TextFormatFlags.WordBreak).Height + Px(12)));
                    for (int i = 0; i < buttons.Length; i++)
                        buttons[i].SetBounds(padding + i * (buttonWidth + small), currentY, buttonWidth, buttonHeight);
                    currentY += buttonHeight + small;
                }
                ActionRow(pauseButton, pinButton);
                ActionRow(explorerButton, rejectButton);
                ActionRow(undoRejectButton, historyButton, statisticsButton);
                currentWallpaperCard.SetBounds(0, nextWallpaperButton.Bottom + gap, width, currentY - small + padding);

                Point scroll = wallpaperPage.AutoScrollPosition;
                wallpaperContent.SetBounds(Math.Max(gap, (wallpaperPage.ClientSize.Width - width) / 2) + scroll.X,
                    gap + scroll.Y, width, currentWallpaperCard.Bottom);
                wallpaperPage.AutoScrollMinSize = new Size(0, wallpaperContent.Height + 2 * gap);
            }
            finally
            {
                wallpaperContent.ResumeLayout();
                wallpaperPage.ResumeLayout(true);
                arrangingWallpaper = false;
            }
            // A newly visible vertical scrollbar reduces the available width.
            if (viewportWidth != wallpaperPage.ClientSize.Width) ArrangeWallpaperPage(scale);
        }
    }
}
