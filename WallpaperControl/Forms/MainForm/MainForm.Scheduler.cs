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
                LogScheduler("start rejected: folder unavailable");
                return;
            }

            string? current = GetCurrentWallpaperPath();

            if (string.IsNullOrWhiteSpace(current) ||
                !File.Exists(current))
            {
                customSlideshowEngineActive = false;
                LogScheduler("start rejected: current wallpaper unavailable");
                return;
            }

            IDesktopWallpaper? wallpaper = null;

            try
            {
                wallpaper =
                    (IDesktopWallpaper)
                    new DesktopWallpaper();

                // Stop native Windows scheduling while retaining the visible image.
                if (!PersistentDesktopTransitionManager.TryStartSession(
                        current, () => wallpaper.SetWallpaper(null, current)))
                {
                    customSlideshowEngineActive = false;
                    LogScheduler("start rejected: desktop session unavailable");
                    return;
                }

                customSlideshowEngineActive = true;
                slideshowPaused = false;
                LogScheduler("started");
                RecalculateCustomSlideshowSchedule();
            }
            catch (Exception ex)
            {
                customSlideshowEngineActive = false;
                LogScheduler("start failed");
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
                LogScheduler("stopped: invalid interval selection");
                customSlideshowPreciseTimer.Change(
                    Timeout.Infinite,
                    Timeout.Infinite);
                return;
            }

            if (customSlideshowLastInterval != milliseconds)
                LogScheduler($"interval changed; newIntervalMs={milliseconds}");
            customSlideshowLastInterval = milliseconds;
            customSlideshowNextChange =
                GetNextAlignedChange(DateTime.Now, milliseconds);

            ArmCustomSlideshowPreciseTimer();
            LogScheduler("deadline planned / resume");
        }

        /// <summary>
        /// Arms or disables the timer according to slideshow, pause, and deadline state.
        /// </summary>
        private void ArmCustomSlideshowPreciseTimer()
        {
            if (!customSlideshowEngineActive ||
                customSlideshowChangeRunning ||
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
            // Early callbacks may re-arm the same deadline several times. Do not spam the log.
            if (schedulerLastArmedDeadline != customSlideshowNextChange)
            {
                schedulerLastArmedDeadline = customSlideshowNextChange;
                LogScheduler($"timer armed; dueMs={remaining.TotalMilliseconds:0}");
            }
        }

        /// <summary>
        /// Marshals the background timer callback to the window&apos;s UI thread.
        /// </summary>
        /// <param name="state">The unused state supplied by the threading timer.</param>
        private void CustomSlideshowPreciseTimerCallback(
            object? state)
        {
            Interlocked.Exchange(ref schedulerCallbackUtcTicks, DateTime.UtcNow.Ticks);
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
            Interlocked.Exchange(ref schedulerDispatchUtcTicks, DateTime.UtcNow.Ticks);
            if (exitRequested || IsDisposed || Disposing) return;
            _ = UpdateFullscreenPauseAsync();
            if (!customSlideshowEngineActive ||
                !fullscreenPolicy.AllowsSlideshow(slideshowPaused) ||
                customSlideshowChangeRunning)
            {
                LogScheduler("automatic skipped: inactive, paused or change in progress; waiting for resume/completion");
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
                if (!schedulerIntervalRetryLogged)
                {
                    schedulerIntervalRetryLogged = true;
                    LogScheduler("automatic skipped: interval selection unavailable; retryMs=250");
                }
                return;
            }

            if (schedulerIntervalRetryLogged)
            {
                schedulerIntervalRetryLogged = false;
                LogScheduler("interval selection recovered");
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

            LogScheduler($"automatic due; latenessMs={(invoked - target).TotalMilliseconds:0}");
            // Alignment is anchored to midnight; skip missed boundaries after sleep
            // or a clock jump instead of replaying them in a rapid catch-up loop.
            customSlideshowNextChange =
                GetNextAlignedChange(
                    invoked,
                    milliseconds);
            LogScheduler("next deadline planned before automatic attempt (retained on skip)");

            _ = AdvanceCustomWallpaperAsync(
                DesktopSlideshowDirection.Forward, automatic: true);

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
