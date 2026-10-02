extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal static class WallpaperWidgetModeTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
    private static Form? Widget(App.WidgetManager manager, string name) => (Form?)typeof(App.WidgetManager).GetField(name, Flags)!.GetValue(manager);
    private static void Repair(App.WidgetManager manager) => typeof(App.WidgetManager).GetMethod("RestoreDesktopWidgetBand", Flags)!.Invoke(manager, null);
    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            string registry = @"Software\WallpaperControl.WidgetModeTests\" + Guid.NewGuid().ToString("N");
            try
            {
                var settings = new App.WidgetSettings
                {
                    NextEnabled = true, WallpaperInfoEnabled = true, ClockEnabled = true,
                    NextLocked = true, WallpaperInfoLocked = true,
                    NextStyle = App.SystemWidgetStyle.Glow, WallpaperInfoStyle = App.SystemWidgetStyle.Clean,
                    NextLocation = new(100, 100), WallpaperInfoLocation = new(170, 100)
                };
                settings.Save(registry);
                string saved = JsonSerializer.Serialize(App.WidgetSettings.Load(registry));
                using (var manager = new App.WidgetManager(() => { }, registryPath: registry))
                {
                    manager.Start();
                    var next = Widget(manager, "nextWidget")!; var info = Widget(manager, "wallpaperInfoWidget")!; var clock = Widget(manager, "clock")!;
                    check(next.Visible && info.Visible, "1. Both enabled widgets are visible in ImageSlideshow");
                    var nextLocation = next.Location; var infoLocation = info.Location;
                    var nextHandle = next.Handle; var infoHandle = info.Handle;
                    manager.SetWallpaperMode(App.WallpaperOperatingMode.VideoWallpaper);
                    check(!next.Visible && !info.Visible, "2. Entering VideoWallpaper immediately hides both widgets");
                    check(clock.Visible, "Independent clock widget stays visible in VideoWallpaper");
                    check(!next.IsDisposed && !info.IsDisposed && next.Handle == nextHandle && info.Handle == infoHandle,
                        "Hidden widgets retain their instances and handles");
                    check(manager.Settings.NextEnabled && manager.Settings.WallpaperInfoEnabled, "Enabled user preferences remain true in VideoWallpaper");
                    using (var editor = new App.WidgetSettingsEditor(manager.Settings))
                        check(editor.ReadWidgetSettings(false).NextEnabled && editor.ReadWidgetSettings(false).WallpaperInfoEnabled,
                            "Settings editor preserves enabled checkboxes during VideoWallpaper");
                    manager.Preview(manager.Settings);
                    check(!next.Visible && !info.Visible && !IsWindowVisible(nextHandle) && !IsWindowVisible(infoHandle),
                        "Settings preview cannot restore native visibility of already-created hidden widgets");
                    check(JsonSerializer.Serialize(App.WidgetSettings.Load(registry)) == saved, "Mode change performs no persisted settings changes");
                    Repair(manager);
                    check(!next.Visible && !info.Visible && !IsWindowVisible(nextHandle) && !IsWindowVisible(infoHandle), "6. Existing Win+D/Desktop repair callback does not restore hidden widgets");
                    manager.SetActivitySuspended(true); manager.SetActivitySuspended(false); Repair(manager);
                    check(!next.Visible && !info.Visible, "7. Fullscreen suspension/resume and repair preserve the VideoWallpaper visibility rule");
                    manager.SetWallpaperMode(App.WallpaperOperatingMode.ImageSlideshow);
                    check(next.Visible && info.Visible, "3. Returning to ImageSlideshow immediately restores both enabled widgets");
                    check(next.Location == nextLocation && info.Location == infoLocation, "Mode roundtrip preserves widget positions");
                    check(manager.Settings.NextLocked && manager.Settings.WallpaperInfoLocked
                        && manager.Settings.NextStyle == settings.NextStyle && manager.Settings.WallpaperInfoStyle == settings.WallpaperInfoStyle,
                        "Mode roundtrip preserves locks and styles");
                    Repair(manager); check(next.Visible && info.Visible, "Restored widgets participate in normal desktop repair again");
                    manager.SetWallpaperMode(App.WallpaperOperatingMode.VideoWallpaper);
                    var preview = manager.Settings; preview.WallpaperInfoEnabled = false; manager.Preview(preview);
                    check(!next.Visible && Widget(manager, "wallpaperInfoWidget") == null, "Disabling Wallpaper-Info during video updates only its user preference");
                    manager.SetWallpaperMode(App.WallpaperOperatingMode.ImageSlideshow);
                    check(next.Visible && Widget(manager, "wallpaperInfoWidget") == null,
                        "4. Returning to ImageSlideshow displays only Next when Wallpaper-Info is disabled");
                    check(App.WidgetSettings.Load(registry).WallpaperInfoEnabled, "Unsaved settings preview remains unsaved across mode changes");
                }
                settings.ClockEnabled = false; settings.Save(registry);
                using (var startup = new App.WidgetManager(() => { }, registryPath: registry))
                {
                    startup.SetWallpaperMode(App.WallpaperOperatingMode.VideoWallpaper); startup.Start();
                    var next = Widget(startup, "nextWidget")!; var info = Widget(startup, "wallpaperInfoWidget")!;
                    check(!next.Visible && !info.Visible, "5. Startup directly in VideoWallpaper keeps both widgets hidden");
                    check(!next.IsHandleCreated && !info.IsHandleCreated, "Video startup never shows or creates native windows for these widgets");
                    int shows = 0;
                    next.VisibleChanged += (_, _) => { if (next.Visible) shows++; };
                    info.VisibleChanged += (_, _) => { if (info.Visible) shows++; };
                    startup.Preview(startup.Settings); Repair(startup); startup.SetActivitySuspended(true); startup.SetActivitySuspended(false);
                    check(shows == 0 && !next.Visible && !info.Visible, "Settings preview, desktop repair and resume do not flash hidden startup widgets");
                    startup.SetWallpaperMode(App.WallpaperOperatingMode.ImageSlideshow);
                    check(next.Visible && info.Visible && shows == 2, "Hidden startup widgets show once each after returning to images");
                    var preview = startup.Settings; preview.NextEnabled = false; startup.Preview(preview);
                    startup.SetWallpaperMode(App.WallpaperOperatingMode.VideoWallpaper); startup.SetWallpaperMode(App.WallpaperOperatingMode.ImageSlideshow);
                    check(Widget(startup, "nextWidget") == null && info.Visible, "Disabled Next stays absent across a mode roundtrip");
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure != null) throw failure;
    }
}
