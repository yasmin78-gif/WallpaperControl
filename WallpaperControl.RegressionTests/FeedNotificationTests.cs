extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Reflection;
using Microsoft.Win32;
internal static class FeedNotificationTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static App.ParsedFeed Snapshot(params string[] ids) => new("Feed",ids.Select(id=>new App.FeedEntry(id,id,"",null,null,null)).ToList());
    internal static void Run(Action<bool,string> check)
    {
        var root = Path.Combine(Path.GetTempPath(),"WCFeedAlerts-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root,"history.json"); var source = new App.FeedSource { Title = "News",Url = "https://example.test/rss",Notifications = true };
            var alerts = new List<int>(); var observer = new App.FeedNotifications(path); observer.NewEntries += (_,count)=>alerts.Add(count); observer.Configure([source]);
            observer.Observe(source,Snapshot("a","b"),false); check(alerts.Count == 0 && File.Exists(path),"First snapshot is silent and identities persist");
            observer.Observe(source,Snapshot("a","b","c","d","e"),false); check(alerts.SequenceEqual([3]),"Multiple new entries produce one bundled notification");
            observer.Observe(source,Snapshot("a","b","c","d","e"),false); check(alerts.Count == 1,"Repeated refresh is deduplicated");
            var restarted = new App.FeedNotifications(path); restarted.NewEntries += (_,count)=>alerts.Add(count); restarted.Configure([source]);
            restarted.Observe(source,Snapshot("a","b","c","d","e","startup"),false); check(alerts.Count == 1,"Restart never reports existing startup entries");
            restarted.Observe(source,Snapshot("startup","c"),false); check(alerts.Count == 1,"Previously notified entry does not repeat after restart");
            restarted.Observe(source,Snapshot("startup","new"),false); check(alerts.SequenceEqual([3,1]),"New entry after restart notifies once");
            var disabled = source with { Notifications = false }; restarted.Configure([disabled]); restarted.Observe(disabled,Snapshot("new","off"),false); check(alerts.Count == 2,"Per-source disabled alerts stay silent");
            restarted.Configure([source]); restarted.Observe(source,Snapshot("off","enabled"),false); check(alerts.Count == 2,"Re-enabling establishes silent baseline");
            restarted.Rebaseline(); restarted.Observe(source,Snapshot("enabled","paused"),false); check(alerts.Count == 2,"First snapshot after fullscreen or power resume has no backlog");
            restarted.Observe(source,Snapshot("paused","suppressed"),true); restarted.Observe(source,Snapshot("suppressed"),false); check(alerts.Count == 2,"Suppressed entries cannot notify later");
            restarted.Observe(source,Snapshot("suppressed","latest"),false); check(alerts.SequenceEqual([3,1,1]),"Alerts resume for later new entries");
            var edited = source with { Url = "https://other.test/rss" }; restarted.Configure([edited]); restarted.Observe(edited,Snapshot("other"),false); check(alerts.Count == 3,"Changed source URL starts a new silent baseline");
            var other = new App.FeedSource { Url = "https://another.test/feed",Notifications = true }; restarted.Configure([edited,other]); restarted.Observe(other,Snapshot("x"),false); restarted.Observe(other,Snapshot("x","y"),false); check(alerts.Count == 4,"Different feeds have independent baselines");
            File.WriteAllText(Path.Combine(root,"bad.json"),"{broken"); var broken = new App.FeedNotifications(Path.Combine(root,"bad.json")); broken.Configure([source]); broken.Observe(source,Snapshot("existing"),false); check(true,"Corrupt history recovers without notifying existing articles");
            var fail = new App.FeedNotifications(root); int failedAlerts = 0; fail.NewEntries += (_,_)=>failedAlerts++; fail.Configure([source]); fail.Observe(source,Snapshot("a"),false); fail.Observe(source,Snapshot("a","b"),false); check(failedAlerts == 0,"Failed durable history save never emits unreliable alert");
        }
        finally { Directory.Delete(root,true); }
        Exception? error = null; var thread = new Thread(()=> { try { Ui(check); } catch(Exception ex) { error = ex; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error != null) throw error;
    }
    private static void Ui(Action<bool,string> check)
    {
        var tones = new App.NotificationSoundPreferences { Twitch = App.NotificationSoundKind.Off,Packages = App.NotificationSoundKind.Soft,Feeds = App.NotificationSoundKind.Bell,Notes = App.NotificationSoundKind.Triad };
        string keyPath = @"Software\WallpaperControl.ToneTests\"+Guid.NewGuid().ToString("N");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath)) key.SetValue("NotificationSound",0);
            var migrated = App.WidgetSettings.Load(keyPath); check(migrated.NotificationTones == App.NotificationSoundPreferences.FromLegacy(App.NotificationSoundKind.Off),"Legacy global sound migrates to all four types");
            var settings = new App.WidgetSettings { NotificationTones = tones }; settings.Save(keyPath);
            check(App.WidgetSettings.Load(keyPath).NotificationTones == tones && settings.Clone().NotificationTones == tones,"Independent sound preferences persist and clone");
            check(tones.ForGroup("twitch") == App.NotificationSoundKind.Off && tones.ForGroup("package") == App.NotificationSoundKind.Soft && tones.ForGroup("feed") == App.NotificationSoundKind.Bell && tones.ForGroup("notes") == App.NotificationSoundKind.Triad,"Each notification type selects its own tone");
            check(!new App.FeedSource().Notifications,"Feed notifications default off");
            using var dialog = new App.SettingsForm(false,"system",3,0x27,3,0x50,3,0x45,7,0x52,"",true,false,true,true,true,80,App.NotificationSoundKind.Chime,tones);
            var combos = new[]{"notificationSound","packageSound","feedSound","noteSound"}.Select(name=>(ComboBox)typeof(App.SettingsForm).GetField(name,Fields)!.GetValue(dialog)!).ToArray();
            check(combos.Select(c=>c.SelectedIndex).SequenceEqual([0,3,4,5]) && combos.All(c=>c.Items.Count==6),"Settings offer independent six-choice tone selectors");
            foreach(var lang in new[]{"de","en","fr","es","ja"})
            {
                typeof(App.SettingsForm).GetMethod("ApplyPreviewLocalization",Fields)!.Invoke(dialog,[lang]);
                check(combos.Select(c=>c.SelectedIndex).SequenceEqual([0,3,4,5]),"Language change preserves all four tones "+lang);
                check(App.Localization.Get("FeedNotifications",lang)!="FeedNotifications" && App.Localization.Get("NotificationSoundTriad",lang)!="NotificationSoundTriad","Feed alert and tone translation "+lang);
            }
            var previewPath = "C:/Users/yasmi/Documents/Codex/2026-09-30/v/outputs/notification-tones.png";
            var tabs = (TabControl)typeof(App.SettingsForm).GetField("settingsTabControl",Fields)!.GetValue(dialog)!; tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page=>(string?)page.Tag=="NotificationSettings");
            typeof(App.SettingsForm).GetMethod("ApplyPreviewLocalization",Fields)!.Invoke(dialog,["de"]);
            dialog.StartPosition=FormStartPosition.Manual; dialog.Location=new(-30000,-30000); dialog.Show(); Application.DoEvents();
            using(var preview = new Bitmap(dialog.Width,dialog.Height)) { dialog.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size)); preview.Save(previewPath); } dialog.Hide();
            typeof(App.SettingsForm).GetMethod("SaveAndClose",Fields)!.Invoke(dialog,null); check(dialog.NotificationTones==tones,"Saving captures each tone independently");
            foreach(var suffix in new[]{"Soft","Bell","Triad"})
            {
                using var stream = typeof(App.NotificationSounds).Assembly.GetManifestResourceStream("WallpaperControl.Notification"+suffix+".wav");
                using var reader = new BinaryReader(stream!); check(new string(reader.ReadChars(4))=="RIFF" && stream!.Length < 90000,"Embedded short PCM tone "+suffix);
            }
            using var icon = new NotifyIcon(); var played = new List<App.NotificationSoundKind>();
            using var queue = new App.DesktopNotifications(icon,()=>false,(_,_)=>{},visible:()=>true,playSound:played.Add,soundForGroup:tones.ForGroup);
            queue.Show("Twitch","silent",()=>{},"twitch"); queue.Show("Package","soft",()=>{},"package"); queue.Show("Feed","bell",()=>{},"feed");
            queue.OnShown(); check(played.Count==0,"Silent Twitch notice never plays tone"); queue.OnClosed(); queue.OnShown(); queue.OnShown(); queue.OnClosed(); queue.OnShown();
            check(played.SequenceEqual([App.NotificationSoundKind.Soft,App.NotificationSoundKind.Bell]),"Queued notices use own type tone exactly once");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath,false); }
    }
}
