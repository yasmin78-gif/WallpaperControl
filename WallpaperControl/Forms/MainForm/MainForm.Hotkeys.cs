using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window hotkeys responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Handles commands forwarded by another application instance on the UI thread.
        /// </summary>
        internal void ExecuteRemoteCommand(string command)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(() => ExecuteRemoteCommand(command));
                return;
            }

            if (string.Equals(command, "show", StringComparison.OrdinalIgnoreCase))
            {
                RestoreFromTray();
                return;
            }

            if (string.Equals(
                command,
                "next",
                StringComparison.OrdinalIgnoreCase))
            {
                AdvanceWallpaper(
                    DesktopSlideshowDirection.Forward);
            }
        }

        /// <summary>
        /// Loads all configured global hotkey combinations.
        /// </summary>
        private void LoadHotkeySettings()
        {
            var settings = appSettings.LoadHotkeySettings();
            hotkeyNextKey = settings.NextKey;
            hotkeyRejectKey = settings.RejectKey;
            hotkeyPauseKey = settings.PauseKey;
            hotkeyRejectModifiers = settings.RejectModifiers;
            hotkeyNextModifiers = settings.NextModifiers;
            hotkeyExplorerKey = settings.ExplorerKey;
            hotkeyExplorerModifiers = settings.ExplorerModifiers;
            hotkeyPauseModifiers = settings.PauseModifiers;
        }

        /// <summary>
        /// Persists all configured global hotkey combinations.
        /// </summary>
        private void SaveHotkeySettings() => appSettings.SaveHotkeySettings(new HotkeySettings
        {
            NextKey = hotkeyNextKey,
            RejectKey = hotkeyRejectKey,
            PauseKey = hotkeyPauseKey,
            RejectModifiers = hotkeyRejectModifiers,
            NextModifiers = hotkeyNextModifiers,
            ExplorerKey = hotkeyExplorerKey,
            ExplorerModifiers = hotkeyExplorerModifiers,
            PauseModifiers = hotkeyPauseModifiers,
        });

        /// <summary>
        /// Builds the native hotkey registrations from the current preferences.
        /// </summary>
        private HotkeyBinding[] GetHotkeyBindings() => new[]
        {
            new HotkeyBinding(HOTKEY_NEXT, hotkeyNextModifiers, hotkeyNextKey, Localization.Get("SettingsHotkeyNext")),
            new HotkeyBinding(HOTKEY_PAUSE, hotkeyPauseModifiers, hotkeyPauseKey, Localization.Get("SettingsHotkeyPause")),
            new HotkeyBinding(HOTKEY_EXPLORER, hotkeyExplorerModifiers, hotkeyExplorerKey, Localization.Get("SettingsHotkeyExplorer")),
            new HotkeyBinding(HOTKEY_REJECT, hotkeyRejectModifiers, hotkeyRejectKey, Localization.Get("SettingsHotkeyReject"))
        };

        /// <summary>
        /// Registers configured shortcuts and optionally reports conflicts.
        /// </summary>
        private void RegisterHotKeys(bool showErrors)
        {
            ShowHotkeyErrors(hotkeyManager.Register(Handle, GetHotkeyBindings()), showErrors);
        }

        /// <summary>
        /// Updates only changed shortcut registrations and optionally reports conflicts.
        /// </summary>
        private void ReRegisterChangedHotKeys(
            bool nextChanged, bool pauseChanged, bool explorerChanged, bool rejectChanged, bool showErrors)
        {
            bool[] changed = { nextChanged, pauseChanged, explorerChanged, rejectChanged };
            HotkeyBinding[] bindings = GetHotkeyBindings().Where((_, index) => changed[index]).ToArray();
            ShowHotkeyErrors(hotkeyManager.Replace(Handle, bindings), showErrors);
        }

        /// <summary>
        /// Displays registration failures when interactive error reporting is enabled.
        /// </summary>
        private void ShowHotkeyErrors(IReadOnlyList<string> failures, bool showErrors)
        {
            if (!showErrors || failures.Count == 0) return;
            MessageBox.Show(this,
                string.Format(Localization.CurrentCulture,
                    Localization.Get("SettingsHotkeyRegistrationFailed"),
                    string.Join(Environment.NewLine, failures.Select(item => "• " + item))),
                "Wallpaper Control", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Releases the global shortcuts owned by this window.
        /// </summary>
        private void UnregisterHotKeys()
        {
            hotkeyManager.Release(Handle, new[] { HOTKEY_NEXT, HOTKEY_PAUSE, HOTKEY_EXPLORER, HOTKEY_REJECT });
        }

        /// <summary>
        /// Dispatches native hotkey messages before passing other messages to the base window.
        /// </summary>
        protected override void WndProc(
            ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id =
                    m.WParam.ToInt32();

                switch (id)
                {
                    case HOTKEY_NEXT:
                        AdvanceWallpaper(
                            DesktopSlideshowDirection.Forward);
                        break;

                    case HOTKEY_PAUSE:
                        TogglePauseFromHotkey();
                        break;

                    case HOTKEY_EXPLORER:
                        ShowCurrentWallpaperInExplorer();
                        break;

                    case HOTKEY_REJECT:
                        if (rejectButton.Enabled)
                        {
                            _ = RejectCurrentWallpaperAsync();
                        }
                        break;
                }

                return;
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// Toggles manual pause from a global shortcut and refreshes the visible state.
        /// </summary>
        private async void TogglePauseFromHotkey()
        {
            if (slideshowPaused)
            {
                await ResumeSlideshowAsync(
                    showError: true);
            }
            else
            {
                PauseSlideshow();
            }

            UpdateCurrentWallpaperDisplay();
            UpdateTrayPauseText();
        }
    }
}
