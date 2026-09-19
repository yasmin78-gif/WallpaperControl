extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

internal static class ActionableAnalyzerTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(Action<bool, string> check)
    {
        FolderFailure(check);
        Persistence(check);
        ThumbnailLifetime(check);
        UnknownFullscreen(check);
        InvariantBackup(check);
        DrawingFailure(check);
    }

    private static void DrawingFailure(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "WallpaperControl-Draw-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (Bitmap source = new(4, 4)) source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Bitmap? allocated = null;
            Action<Bitmap> fail = bitmap => { allocated = bitmap; throw new InvalidOperationException("Injected drawing failure"); };
            MethodInfo thumbnail = typeof(App.StatisticsForm).GetMethod("CreateThumbnail", BindingFlags.Static | BindingFlags.NonPublic)!;
            check(thumbnail.Invoke(null, new object[] { path, fail }) == null && allocated != null && Disposed(allocated),
                "A01 thumbnail failure after allocation disposes its bitmap and returns null");
            allocated = null;
            MethodInfo frame = typeof(App.PersistentDesktopWallpaperHost).GetMethod("LoadFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
            bool propagated = false;
            try { frame.Invoke(null, new object[] { path, new Size(8, 8), App.DesktopWallpaperPosition.Fill, fail }); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { propagated = true; }
            check(propagated && allocated != null && Disposed(allocated),
                "A01 frame drawing failure disposes its bitmap and propagates the failure");
            using var successful = (Bitmap)frame.Invoke(null,
                new object?[] { path, new Size(8, 8), App.DesktopWallpaperPosition.Fill, null })!;
            check(successful.Size == new Size(8, 8) && !Disposed(successful),
                "A01 successful frame rendering transfers a live bitmap to its caller");
        }
        finally { File.Delete(path); }
    }

    private static void FolderFailure(Action<bool, string> check)
    {
        string keyPath = @"Software\WallpaperControl.ActionableTests-" + Guid.NewGuid().ToString("N");
        try
        {
            var form = (App.MainForm)RuntimeHelpers.GetUninitializedObject(typeof(App.MainForm));
            var store = new App.AppSettingsStore(keyPath);
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            long revision = App.SettingsPersistence.FailureRevision;
            store.SaveLastWallpaperFolder(missing);
            check(App.SettingsPersistence.FailureRevision == revision, "B01 successful save does not report failure");
            typeof(App.MainForm).GetField("appSettings", Instance)!.SetValue(form, store);
            typeof(App.MainForm).GetField("slideshowPaused", Instance)!.SetValue(form, true);
            bool assigned = (bool)typeof(App.MainForm).GetMethod("SetWallpaperFolder", Instance)!
                .Invoke(form, new object[] { missing, false })!;
            check(!assigned, "A02 missing folder assignment returns failure without a dialog");
            var resume = (Task<bool>)typeof(App.MainForm).GetMethod("ResumeSlideshowAsync", Instance)!
                .Invoke(form, new object[] { false })!;
            check(!resume.GetAwaiter().GetResult(), "A02 resume propagates folder assignment failure");
            check((bool)typeof(App.MainForm).GetField("slideshowPaused", Instance)!.GetValue(form)!,
                "A02 failed resume preserves manual pause");
            check(store.LoadLastWallpaperFolder() == missing, "A02 failed assignment preserves saved source");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
    }

    private static void Persistence(Action<bool, string> check)
    {
        // Windows rejects a registry path segment longer than 255 characters.
        var store = new App.AppSettingsStore(new string('x', 256));
        Action[] saves =
        {
            () => store.SaveLastWallpaperFolder("private-path"),
            () => store.SaveCloseToTraySetting(true),
            () => store.SaveAutomaticUpdateCheckSetting(true),
            () => store.SaveThemeMode("dark"),
            () => store.SaveWindowOpacityPercent(90),
            () => store.SaveHotkeySettings(new App.HotkeySettings()),
            () => store.SaveRejectSettings(new App.RejectSettings()),
            () => store.SavePauseOnFullscreen(true),
            () => store.SaveWindowPosition(new Point(20, 30)),
            () => new App.WidgetSettings().Save(new string('x', 256))
        };
        foreach (Action save in saves)
        {
            long before = App.SettingsPersistence.FailureRevision;
            save();
            check(App.SettingsPersistence.FailureRevision == before + 1,
                "B01 rejected settings write reports exactly one non-modal failure");
        }
        var partial = new App.WidgetSettings();
        typeof(App.WidgetSettings).GetField("loadFailed", Instance)!.SetValue(partial, true);
        long revision = App.SettingsPersistence.FailureRevision;
        partial.Save(new string('x', 256));
        check(App.SettingsPersistence.FailureRevision == revision + 1,
            "B01 refusing to overwrite partially loaded preferences is reported");
    }

    private static bool Disposed(Bitmap bitmap)
    {
        try { _ = bitmap.GetPixel(0, 0); return false; }
        catch (ArgumentException) { return true; }
    }

    private static void ThumbnailLifetime(Action<bool, string> check)
    {
        using var form = new App.StatisticsForm(false, 100, new Dictionary<string, int>(),
            new Dictionary<string, DateTime>(), new Dictionary<string, Dictionary<string, int>>(),
            new Dictionary<string, int>(), new Dictionary<string, double>(), "",
            DateTime.Now, DateTime.Now, DateTime.Now);
        _ = form.Handle;
        MethodInfo deliver = typeof(App.StatisticsForm).GetMethod("DeliverThumbnail", Instance)!;
        var preview = (Form)typeof(App.StatisticsForm).GetField("wallpaperPreviewForm", Instance)!.GetValue(form)!;
        var menu = (ContextMenuStrip)typeof(App.StatisticsForm).GetField("rowContextMenu", Instance)!.GetValue(form)!;
        using Bitmap cached = new(2, 2);
        deliver.Invoke(form, new object[] { "cached", cached });
        Application.DoEvents();
        check(!Disposed(cached), "B03 delivered thumbnail stays alive while owned by the open form");
        using Bitmap duplicate = new(2, 2);
        deliver.Invoke(form, new object[] { "cached", duplicate });
        Application.DoEvents();
        check(Disposed(duplicate) && !Disposed(cached), "B03 duplicate result is disposed without replacing the cached image");
        using Bitmap pending = new(2, 2);
        deliver.Invoke(form, new object[] { "pending", pending });
        form.Dispose(); // Deliberately do not dispatch the pending callback first.
        check(Disposed(cached), "B03 direct Dispose releases cached thumbnails");
        check(Disposed(pending), "B03 direct Dispose releases an undispatched thumbnail");
        check(preview.IsDisposed && menu.IsDisposed, "B03 direct Dispose releases the owned preview and context menu");
        using Bitmap late = new(2, 2);
        deliver.Invoke(form, new object[] { "late", late });
        check(Disposed(late), "B03 image completed after disposal is released immediately");
        form.Dispose();
        Application.DoEvents();
        check(form.IsDisposed, "B03 repeated disposal and queued callbacks are safe");
    }

    private static void UnknownFullscreen(Action<bool, string> check)
    {
        var policy = new App.FullscreenPausePolicy();
        DateTime now = DateTime.UtcNow;
        check(!policy.Update(true, null, now) && !policy.IsPaused,
            "B04 an unknown initial sample does not invent fullscreen");
        policy.Update(true, true, now);
        policy.Update(true, false, now.AddMilliseconds(250));
        check(!policy.Update(true, null, now.AddMilliseconds(750)) && policy.IsPaused,
            "B04 failed native sample cannot release fullscreen pause");
        check(!policy.Update(true, false, now.AddMilliseconds(1000)) && policy.IsPaused,
            "B04 failed sample resets the clear-period timer");
        check(policy.Update(true, false, now.AddMilliseconds(1500)) && !policy.IsPaused,
            "B04 confirmed clear samples retain the 500 ms resume timing");
        policy.Update(true, true, now);
        check(policy.Update(false, null, now) && !policy.IsPaused,
            "B04 disabling protection works even when detection is unknown");
    }

    private static void InvariantBackup(Action<bool, string> check)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        string directory = Path.Combine(Path.GetTempPath(), "WallpaperControl-Culture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (string culture in new[] { "th-TH", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                string path = Path.Combine(directory, culture + ".json");
                File.WriteAllText(path, "damaged");
                typeof(App.StatisticsStorage).GetMethod("PreserveDamagedFile", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { path });
                string backup = Directory.GetFiles(directory, culture + ".json.corrupt-*").Single();
                check(Path.GetFileName(backup).StartsWith(culture + ".json.corrupt-" +
                    DateTime.UtcNow.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal),
                    "B06 damaged statistics use a Gregorian backup timestamp under " + culture);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            Directory.Delete(directory, true);
        }
    }
}
