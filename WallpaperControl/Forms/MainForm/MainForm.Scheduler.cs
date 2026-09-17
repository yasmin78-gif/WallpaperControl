using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Main-window scheduler responsibilities; see README.md in this directory for the code map.
    public partial class MainForm
    {
        /// <summary>
        /// Enables application-controlled wallpaper scheduling for the current folder.
        /// </summary>
        private void StartCustomSlideshowEngine()
        {
            string folder = folderTextBox.Text;

            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                customSlideshowEngineActive = false;
                return;
            }

            string? current = GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(current) ||
                !File.Exists(current))
            {
                customSlideshowEngineActive = false;
                return;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                // Stop native Windows scheduling while retaining the visible image.
                wallpaper.SetWallpaper(null, current);

                customSlideshowEngineActive = true;
                slideshowPaused = false;
                RecalculateCustomSlideshowSchedule();
            }
            catch (Exception ex)
            {
                customSlideshowEngineActive = false;
                AppLogger.Error("Could not start the custom slideshow engine.", ex);
            }
            finally
            {
                ReleaseComObject(wallpaper);
            }
        }

        /// <summary>
        /// Computes the next clock-aligned change and re-arms the precise timer.
        /// </summary>
        private void RecalculateCustomSlideshowSchedule()
        {
            if (!TryGetSelectedInterval(out uint milliseconds))
            {
                customSlideshowNextChange = DateTime.MaxValue;
                customSlideshowPreciseTimer.Change(
                    Timeout.Infinite,
                    Timeout.Infinite);
                return;
            }

            customSlideshowLastInterval = milliseconds;
            customSlideshowNextChange =
                GetNextAlignedChange(DateTime.Now, milliseconds);

            ArmCustomSlideshowPreciseTimer();
        }

        /// <summary>
        /// Arms or disables the timer according to slideshow, pause, and deadline state.
        /// </summary>
        private void ArmCustomSlideshowPreciseTimer()
        {
            if (!customSlideshowEngineActive ||
                !fullscreenPolicy.AllowsSlideshow(slideshowPaused) ||
                customSlideshowNextChange == DateTime.MaxValue)
            {
                customSlideshowPreciseTimer.Change(
                    Timeout.Infinite,
                    Timeout.Infinite);
                return;
            }

            TimeSpan remaining =
                customSlideshowNextChange - DateTime.Now;

            if (remaining < TimeSpan.Zero)
            {
                remaining =
                    TimeSpan.Zero;
            }

            // Use a one-shot timer and re-arm it for each aligned interval boundary.
            customSlideshowPreciseTimer.Change(
                remaining,
                Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Marshals the background timer callback to the window&apos;s UI thread.
        /// </summary>
        /// <param name="state">The unused state supplied by the threading timer.</param>
        private void CustomSlideshowPreciseTimerCallback(
            object? state)
        {
            if (IsDisposed ||
                Disposing)
            {
                return;
            }

            try
            {
                BeginInvoke(
                    new Action(
                        ProcessPreciseCustomSlideshowTick));
            }
            catch (Exception ex)
            {
                // During shutdown BeginInvoke can legitimately fail. Outside
                // shutdown, keep the failure for diagnostics.
                if (!IsDisposed && !Disposing)
                {
                    AppLogger.Warning("Could not dispatch the precise slideshow timer callback.", ex);
                }
            }
        }

        /// <summary>
        /// Rechecks fullscreen and timing guards before requesting a scheduled wallpaper change.
        /// </summary>
        private void ProcessPreciseCustomSlideshowTick()
        {
            _ = UpdateFullscreenPauseAsync();
            if (!customSlideshowEngineActive ||
                !fullscreenPolicy.AllowsSlideshow(slideshowPaused) ||
                customSlideshowChangeRunning)
            {
                ArmCustomSlideshowPreciseTimer();
                return;
            }

            if (!TryGetSelectedInterval(out uint milliseconds))
            {
                // UI updates can temporarily clear the interval selection. Retry after
                // a short delay so the one-shot timer neither stops permanently
                // nor enters a tight retry loop.
                customSlideshowPreciseTimer.Change(
                    TimeSpan.FromMilliseconds(250),
                    Timeout.InfiniteTimeSpan);
                return;
            }

            if (milliseconds != customSlideshowLastInterval)
            {
                RecalculateCustomSlideshowSchedule();
                return;
            }

            DateTime target =
                customSlideshowNextChange;

            DateTime invoked =
                DateTime.Now;

            if (invoked < target)
            {
                ArmCustomSlideshowPreciseTimer();
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"Custom slideshow target: {target:HH:mm:ss.fff}; " +
                $"callback: {invoked:HH:mm:ss.fff}; " +
                $"delta: {(invoked - target).TotalMilliseconds:+0;-0;0} ms");

            _ = AdvanceCustomWallpaperAsync(
                DesktopSlideshowDirection.Forward);

            // Derive the next deadline from the intended boundary to avoid timing drift.
            customSlideshowNextChange =
                GetNextAlignedChange(
                    target.AddMilliseconds(1),
                    milliseconds);

            ArmCustomSlideshowPreciseTimer();
        }

        /// <summary>
        /// Returns the next interval boundary measured from the start of the supplied day.
        /// </summary>
        /// <param name="now">The current time used for the scheduling or pause decision.</param>
        /// <param name="intervalMilliseconds">The slideshow interval in milliseconds.</param>
        /// <returns>The first interval boundary strictly after the supplied time, aligned to midnight.</returns>
        private static DateTime GetNextAlignedChange(
            DateTime now,
            uint intervalMilliseconds)
        {
            long intervalTicks =
                TimeSpan.FromMilliseconds(intervalMilliseconds).Ticks;

            DateTime dayStart = now.Date;
            long elapsedTicks = (now - dayStart).Ticks;
            long completedIntervals = elapsedTicks / intervalTicks;
            long nextTicks = (completedIntervals + 1) * intervalTicks;

            return dayStart.AddTicks(nextTicks);
        }

        /// <summary>
        /// Reads the selected interval in milliseconds when a valid option is selected.
        /// </summary>
        /// <param name="milliseconds">Receives the selected interval in milliseconds, or zero when no valid selection exists.</param>
        /// <returns>True when a valid interval is selected; otherwise, false.</returns>
        private bool TryGetSelectedInterval(out uint milliseconds)
        {
            milliseconds = 0;

            if (intervalComboBox.SelectedItem is not DisplayOption<uint> selected)
                return false;

            milliseconds = selected.Value;
            return true;
        }
    }
}
