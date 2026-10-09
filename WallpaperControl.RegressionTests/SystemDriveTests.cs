extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using Microsoft.Win32;
using System.Drawing;
using System.Reflection;
internal static class SystemDriveTests
{
    private const BindingFlags Fields = BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object instance,string name) => (T)instance.GetType().GetField(name,Fields)!.GetValue(instance)!;
    internal static void Run(Action<bool,string> check)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { Ui(check); } catch(Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error != null) throw error;
    }
    private static void Ui(Action<bool,string> check)
    {
        string path = @"Software\WallpaperControl.DriveTests\"+Guid.NewGuid().ToString("N");
        try
        {
            var legacy = App.WidgetSettings.Load(path);
            check(legacy.SystemSelectedDrives.SequenceEqual(App.SystemDriveSelection.Default) && legacy.SystemDriveWarningPercent == 10,"Missing drive preferences migrate to actual system drive and 10% free warning");
            check(App.SystemDriveSelection.Normalize(["d:\\"," C: ","d:","\\\\server\\share","C:\\folder","", "1:"]).SequenceEqual(["C:","D:"]),"Only drive roots are retained and normalized without duplicates");
            check(App.SystemDriveSelection.Parse("{bad").SequenceEqual(App.SystemDriveSelection.Default),"Corrupt drive settings recover to system drive");
            check(App.SystemDriveSelection.Parse("[]").Length == 0,"Explicit empty selection does not select a drive implicitly");
            check(App.SystemDriveSelection.IsLocal(DriveType.Fixed) && App.SystemDriveSelection.IsLocal(DriveType.Removable) && !App.SystemDriveSelection.IsLocal(DriveType.Network) && !App.SystemDriveSelection.IsLocal(DriveType.CDRom),"Local fixed/removable drives are supported, network/optical drives excluded");
            var values = new App.WidgetSettings { SystemSelectedDrives = ["F:","C:","D:"],SystemDriveWarningPercent = 15 };
            values.Save(path); var loaded = App.WidgetSettings.Load(path);
            check(loaded.SystemSelectedDrives.SequenceEqual(["C:","D:","F:"]) && loaded.SystemDriveWarningPercent == 15,"Multiple drive choices and custom warning persist");
            var clone = loaded.Clone(); clone.SystemSelectedDrives[0] = "Z:";
            check(loaded.SystemSelectedDrives[0] == "C:","Cloned settings own their drive selection");
            const long gb = 1024L*1024*1024;
            var drives = new[] { new App.DriveSnapshot("F:",50*gb,1000*gb),new App.DriveSnapshot("D:",100*gb,1000*gb),new App.DriveSnapshot("C:",700*gb,1000*gb) };
            check(App.SystemDriveSelection.Visible(drives,["C:","F:"]).Select(d=>d.Name).SequenceEqual(["C:","F:"]),"Only selected drives display in alphabetical order");
            check(!App.SystemDriveSelection.Warning(drives[1],10) && App.SystemDriveSelection.Warning(drives[0],10),"10% boundary remains normal while less than 10% warns");
            check(App.SystemDriveSelection.Warning(drives[1],15) && !App.SystemDriveSelection.Warning(drives[0],3),"Custom free-space warning threshold changes red state");
            check(!App.SystemDriveSelection.Warning(new("X:",0,0),10) && App.SystemDriveSelection.Visible([new("X:",0,0)],["X:"]).Length == 0,"Unavailable/zero capacity never divides by zero or displays misleading usage");
            check(App.SystemDriveSelection.Visible(drives,["F:"]).Length == 1 && App.SystemDriveSelection.Visible(drives.Take(2).Where(d=>d.Name!="F:"),["F:"]).Length == 0,"Disconnected drive disappears and returns when available");
            using var editor = new App.WidgetSettingsEditor(values);
            var list = Field<CheckedListBox>(editor,"systemDriveList"); var warning = Field<NumericUpDown>(editor,"systemDriveWarning");
            check(list.CheckedItems.Cast<string>().Order().SequenceEqual(["C:","D:","F:"]),"Settings keep disconnected selected drive letters");
            list.SetItemChecked(list.Items.IndexOf("D:"),false); warning.Value = 20;
            var edited = editor.ReadWidgetSettings(false);
            check(edited.SystemSelectedDrives.SequenceEqual(["C:","F:"]) && edited.SystemDriveWarningPercent == 20,"Checkbox and warning edits update settings immediately");
            using var manager = new App.WidgetManager(()=>{},registryPath:path);
            manager.Preview(edited); check(manager.Settings.SystemSelectedDrives.SequenceEqual(["C:","F:"]),"Live preview applies selected drives");
            manager.CancelPreview(loaded); check(manager.Settings.SystemSelectedDrives.SequenceEqual(["C:","D:","F:"]) && manager.Settings.SystemDriveWarningPercent == 15,"Discard restores drive selection and warning threshold");
            manager.CommitPreview(edited); check(App.WidgetSettings.Load(path).SystemSelectedDrives.SequenceEqual(["C:","F:"]),"Saving commits selected drive preferences");
            using var widget = new App.SystemWidgetForm(true,5,App.SystemWidgetStyle.Glow,false,false,false,false,false,true,Point.Empty,_=>{},["C:","D:","F:"],10);
            typeof(App.SystemWidgetForm).GetField("snapshot",Fields)!.SetValue(widget,new App.SystemMonitorSnapshot { Drives = drives });
            foreach(var style in Enum.GetValues<App.SystemWidgetStyle>())
            {
                widget.Apply(true,5,style,false,false,false,false,false,true,["C:","D:","F:"],10);
                using var bitmap = widget.RenderBitmap(); check(bitmap.Width == 330 && bitmap.Height == 184,$"Three drive rows fit {style} widget");
                var color = bitmap.GetPixel(100,155); check(color.R > color.G && color.R > color.B,$"Low-free-space F: bar is red in {style}");
            }
            string? preview = Environment.GetEnvironmentVariable("WALLPAPER_DRIVE_PREVIEW"); if(preview != null) { using var bitmap = widget.RenderBitmap(); bitmap.Save(preview); }
            widget.Apply(true,5,App.SystemWidgetStyle.Clean,false,false,false,false,false,true,["C:"],10);
            using(var single = widget.RenderBitmap()) check(single.Height == 108,"Deselecting drives reduces widget height");
            widget.Apply(true,5,App.SystemWidgetStyle.Clean,false,false,false,false,false,true,[],10);
            using(var none = widget.RenderBitmap()) check(none.Height == 108,"Empty selection draws no fallback drive");
            foreach(string language in new[] {"de","en","fr","es","ja"})
            {
                editor.ApplyPresentation(true,language); check(editor.ReadWidgetSettings(false).SystemSelectedDrives.SequenceEqual(["C:","F:"]),$"Localization preserves drive selection in {language}");
            }
            editor.ApplyPresentation(true,"de"); check(list.BackColor == App.AppTheme.InputBackground(true) && list.ForeColor == App.AppTheme.TextPrimary(true),"Drive checklist follows dark theme");
            editor.ApplyPresentation(false,"de"); check(list.BackColor == App.AppTheme.InputBackground(false) && list.ForeColor == App.AppTheme.TextPrimary(false),"Drive checklist follows light theme");
            Console.WriteLine("Available local drives: "+string.Join(", ",App.SystemDriveSelection.Available()));
            var native = App.SystemMonitorService.ReadDrives();
            check(native.All(d => d.TotalBytes > 0 && d.FreeBytes >= 0 && d.FreeBytes <= d.TotalBytes),"Native local-drive capacity sampling returns valid counters");
            check(native.All(d => App.SystemDriveSelection.Available().Contains(d.Name)),"Native sampling excludes network/optical drives");
            Console.WriteLine("Native drive readings: "+string.Join(", ",native.Select(d => $"{d.Name} {d.FreeBytes/gb} GiB free / {d.TotalBytes/gb} GiB")));
            string? settingsPreview = Environment.GetEnvironmentVariable("WALLPAPER_DRIVE_SETTINGS_PREVIEW");
            if (settingsPreview != null)
            {
                using var form = new Form { ClientSize = new(1050,740),StartPosition = FormStartPosition.Manual,Location = new(-30000,-30000) };
                var host = new App.WidgetSettingsEditor(values) { Dock = DockStyle.Fill }; form.Controls.Add(host); host.ApplyPresentation(true,"de"); host.SelectWidget("SettingsNavSystem");
                form.Show(); Application.DoEvents(); using var image = new Bitmap(form.Width,form.Height); form.DrawToBitmap(image,form.ClientRectangle); image.Save(settingsPreview); form.Hide();
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path,false); }
    }
}
