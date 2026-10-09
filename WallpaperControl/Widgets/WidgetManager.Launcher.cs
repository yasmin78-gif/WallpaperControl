namespace WallpaperControl;
internal sealed partial class WidgetManager
{
    private LauncherWidgetForm? launcherWidget;
    private LauncherShortcutStore? launcherShortcuts;
    private bool launcherShortcutsMigrated;
    private LauncherShortcutStore LauncherShortcuts => launcherShortcuts ??= new(registryPath==WidgetSettings.RegistryPath?LauncherShortcutStore.DefaultDirectory:
        Path.Combine(Path.GetTempPath(),"WallpaperControl.LauncherTests",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(registryPath)))));
    internal Action? LauncherEntriesChanged { get; set; }
    internal List<LauncherEntry> LauncherEntries=>settings.Launcher.Entries.ToList();
    internal void SaveLauncherEntries(List<LauncherEntry> entries)
    {
        try
        {
            var persisted=previewMode?WidgetSettings.Load(registryPath):settings;
            var next=LauncherShortcuts.Update(settings.Launcher.Entries,entries,value=>persisted.SaveLauncherEntries(value,registryPath));
            if(next==null)return;
            settings.Launcher.Entries=next; launcherWidget?.Apply(settings.Launcher,settings.ClockLanguageCode); LauncherEntriesChanged?.Invoke();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { AppLogger.Info($"Launcher shortcut import failed: type={ex.GetType().Name}"); MessageBox.Show(Localization.Get("LauncherShortcutFailed",settings.ClockLanguageCode),Localization.Get("LauncherTitle",settings.ClockLanguageCode),MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }
    private void ShowLauncherManager()
    {
        using var dialog=new Form { Text=Localization.Get("LauncherManage",settings.ClockLanguageCode),ClientSize=new(620,330),StartPosition=FormStartPosition.CenterScreen,ShowInTaskbar=false,MinimizeBox=false };
        using var list=new LauncherEntryList(()=>LauncherEntries,SaveLauncherEntries,settings.ClockLanguageCode,settings.Launcher.Style,NotesDialogStyle.ResolveDarkMode()) { Dock=DockStyle.Fill };
        var close=new Button { Text=Localization.Get("ClipboardClose",settings.ClockLanguageCode),Dock=DockStyle.Bottom,Height=36,DialogResult=DialogResult.OK };
        dialog.Controls.Add(list); dialog.Controls.Add(close); dialog.CancelButton=close; NotesDialogStyle.Apply(dialog,settings.Launcher.Style,NotesDialogStyle.ResolveDarkMode()); dialog.ShowDialog();
    }
    private void ApplyLauncherWidget(WidgetSettings target,bool restoreLocations)
    {
        if(!launcherShortcutsMigrated)
        {
            launcherShortcutsMigrated=true;
            if(settings.Launcher.Entries.Any(e=>!e.ManagedShortcut&&!LauncherTargets.IsWebsite(e.Target)&&Path.GetExtension(e.Target).Equals(".lnk",StringComparison.OrdinalIgnoreCase)&&System.IO.File.Exists(e.Target)))
            {
                try
                {
                    var persisted=previewMode?WidgetSettings.Load(registryPath):settings;
                    var next=LauncherShortcuts.Update(settings.Launcher.Entries,settings.Launcher.Entries,value=>persisted.SaveLauncherEntries(value,registryPath),tolerateMissing:true);
                    if(next!=null) { settings.Launcher.Entries=next; target.Launcher.Entries=next; LauncherEntriesChanged?.Invoke(); }
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
                { AppLogger.Info($"Launcher shortcut migration failed: type={ex.GetType().Name}"); }
            }
        }
        if(!target.Launcher.Enabled) { DisposeLauncher(); return; }
        if(launcherWidget==null||launcherWidget.IsDisposed)
        {
            launcherWidget=new(target.Launcher,target.ClockLanguageCode,point=> { settings.Launcher.Location=point; if(!previewMode)settings.Save(registryPath); },SaveLauncherEntries,ShowLauncherManager);
            launcherWidget.SetActivitySuspended(activitySuspended); launcherWidget.SetPowerSuspended(packagePowerSuspended);
            RegisterDesktopWidget(launcherWidget); launcherWidget.Show();
            if(!DesktopWidgetNative.AttachToDesktop(launcherWidget,target.Launcher.Location)) { launcherWidget.Hide(); AppLogger.Info("Launcher desktop attachment failed"); }
        }
        else { launcherWidget.Apply(target.Launcher,target.ClockLanguageCode); if(restoreLocations)launcherWidget.Location=WidgetSettings.EnsureVisible(target.Launcher.Location,launcherWidget.Size); DesktopWidgetNative.KeepOnDesktop(launcherWidget); }
        if(previewMode)DesktopWidgetNative.EnableInteraction(launcherWidget);
    }
    private void DisposeLauncher() { launcherWidget?.Close(); launcherWidget?.Dispose(); launcherWidget=null; }
}
