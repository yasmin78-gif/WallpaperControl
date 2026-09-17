using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal enum UpdateDialogKind
    {
        UpdateAvailable,
        UpToDate,
        Failed
    }

    internal sealed class UpdateDialog : Form
    {
        private readonly bool darkMode;

        /// <summary>
        /// Builds the release-check result dialog using the requested language, theme, and version details.
        /// </summary>
        /// <param name="kind">The result category that determines the dialog content and actions.</param>
        /// <param name="currentVersion">The version of the running application displayed in the dialog.</param>
        /// <param name="latestVersion">The discovered release version, when available.</param>
        /// <param name="darkMode">True to use the dark palette; false to use the light palette.</param>
        /// <param name="windowOpacityPercent">The window opacity as a percentage.</param>
        /// <param name="languageCode">The language code used for localized text.</param>
        public UpdateDialog(
            UpdateDialogKind kind,
            string currentVersion,
            string? latestVersion,
            bool darkMode,
            int windowOpacityPercent,
            string? languageCode = null)
        {
            this.darkMode = darkMode;

            // Reads a localized update-dialog label.
            string Get(string key) =>
                languageCode == null
                    ? Localization.Get(key)
                    : Localization.Get(key, languageCode);

            string title;
            string message;
            string glyph;

            switch (kind)
            {
                case UpdateDialogKind.UpdateAvailable:
                    title = string.Format(
                        Get("UpdateCheckAvailableTitle"),
                        latestVersion ?? currentVersion);
                    message = string.Format(
                        Get("UpdateCheckAvailableMessage"),
                        currentVersion,
                        latestVersion ?? currentVersion);
                    glyph = "↓";
                    break;

                case UpdateDialogKind.UpToDate:
                    title = Get("UpdateCheckUpToDateTitle");
                    message = string.Format(
                        Get("UpdateCheckUpToDateMessage"),
                        currentVersion);
                    glyph = "✓";
                    break;

                default:
                    title = Get("UpdateCheckFailedTitle");
                    message = Get("UpdateCheckFailedMessage");
                    glyph = "!";
                    break;
            }

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 250);
            Font = new Font("Segoe UI", 10F);
            Opacity = Math.Clamp(windowOpacityPercent, 20, 100) / 100.0;

            Color background = AppTheme.WindowBackground(darkMode);
            Color panelBackground = AppTheme.PanelBackground(darkMode);
            Color foreground = AppTheme.TextPrimary(darkMode);
            Color secondary = AppTheme.TextSecondary(darkMode);

            BackColor = background;
            ForeColor = foreground;

            Label iconLabel = new Label
            {
                Text = glyph,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(27, 28),
                Size = new Size(62, 62),
                Font = new Font("Segoe UI Symbol", 34F, FontStyle.Bold),
                ForeColor = kind == UpdateDialogKind.Failed
                    ? (darkMode ? AppTheme.DarkDanger : Color.Firebrick)
                    : (darkMode ? AppTheme.DarkAccentSoft : Color.FromArgb(25, 105, 190))
            };
            Controls.Add(iconLabel);

            Label titleLabel = new Label
            {
                Text = title,
                Location = new Point(96, 28),
                Size = new Size(395, 34),
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = foreground,
                AutoEllipsis = true
            };
            Controls.Add(titleLabel);

            Label messageLabel = new Label
            {
                Text = message,
                Location = new Point(99, 72),
                Size = new Size(390, 78),
                ForeColor = secondary
            };
            Controls.Add(messageLabel);

            Panel buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                BackColor = panelBackground
            };
            Controls.Add(buttonPanel);

            if (kind == UpdateDialogKind.UpdateAvailable)
            {
                Button laterButton = CreateButton(
                    Get("UpdateDialogLaterButton"),
                    new Point(278, 18),
                    100,
                    false);
                laterButton.DialogResult = DialogResult.Cancel;
                buttonPanel.Controls.Add(laterButton);

                Button releaseButton = CreateButton(
                    Get("UpdateDialogViewReleaseButton"),
                    new Point(388, 18),
                    112,
                    true);
                releaseButton.DialogResult = DialogResult.OK;
                buttonPanel.Controls.Add(releaseButton);

                AcceptButton = releaseButton;
                CancelButton = laterButton;
            }
            else
            {
                Button okButton = CreateButton(
                    Get("UpdateDialogOkButton"),
                    new Point(400, 18),
                    100,
                    true);
                okButton.DialogResult = DialogResult.OK;
                buttonPanel.Controls.Add(okButton);
                AcceptButton = okButton;
                CancelButton = okButton;
            }

            Shown += (_, _) => ApplyTitleBarTheme();
        }

        /// <summary>
        /// Creates a themed dialog action with the requested bounds and visual emphasis.
        /// </summary>
        /// <param name="text">The text to display, format, or parse.</param>
        /// <param name="location">The widget position in screen coordinates.</param>
        /// <param name="width">The width in pixels.</param>
        /// <param name="primary">Whether the button should use the primary-action styling.</param>
        /// <returns>The configured action button owned by the dialog.</returns>
        private Button CreateButton(
            string text,
            Point location,
            int width,
            bool primary)
        {
            Button button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(width, 36),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                ForeColor = AppTheme.TextPrimary(darkMode),
                BackColor = primary && darkMode
                    ? AppTheme.DarkSelection
                    : AppTheme.ControlBackground(darkMode)
            };

            button.FlatAppearance.BorderColor = primary
                ? (darkMode ? AppTheme.DarkAccentSoft : Color.FromArgb(80, 130, 185))
                : AppTheme.Border(darkMode);
            button.FlatAppearance.MouseOverBackColor = AppTheme.ControlHover(darkMode);
            button.FlatAppearance.MouseDownBackColor = AppTheme.ControlPressed(darkMode);
            return button;
        }

        /// <summary>
        /// Applies the current theme to the native update dialog title bar.
        /// </summary>
        private void ApplyTitleBarTheme()
        {
            WindowsTheme.ApplyTitleBar(this, darkMode);
        }
    }
}
