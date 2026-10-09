extern alias WallpaperApp;
using App=WallpaperApp::WallpaperControl;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
internal static class LauncherWidgetTests
{
    internal static void Run(Action<bool,string> check)
    {
        Exception? error=null; var thread=new Thread(()=> { try { Ui(check); } catch(Exception ex) { error=ex; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error!=null)throw error;
    }
    private static void Ui(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"WallpaperControl.LauncherChecks-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string registry=@"Software\WallpaperControl.LauncherChecks-"+Guid.NewGuid().ToString("N");
        try
        {
            var document=Path.Combine(root,"file with spaces.txt"); System.IO.File.WriteAllText(document,"test");
            var program=Environment.ProcessPath!;
            var folder=App.LauncherTargets.Create("Folder",root,""); var file=App.LauncherTargets.Create("File",document,""); var web=App.LauncherTargets.Create("GitHub","https://github.com/","");
            var exe=App.LauncherTargets.Create("Check",program,"--launcher-child");
            check(!new App.LauncherSettings().Enabled,"Launcher disabled by default with no entries");
            check(App.LauncherTargets.Kind(folder.Target)==App.LauncherTargetKind.Folder&&App.LauncherTargets.Kind(file.Target)==App.LauncherTargetKind.File&&App.LauncherTargets.Kind(web.Target)==App.LauncherTargetKind.Website&&App.LauncherTargets.Kind(exe.Target)==App.LauncherTargetKind.Program,"Program, file, folder and website detected");
            check(App.LauncherTargets.Create("",document,"").Name=="file with spaces","Blank entry name derives from target");
            check(App.LauncherTargets.Resolve("notepad.exe").EndsWith("notepad.exe",StringComparison.OrdinalIgnoreCase),"Bare executable resolves through PATH");
            foreach(var invalid in new[]{"javascript:alert(1)","file:///C:/Windows/notepad.exe","ftp://example.com/a","https://","notepad.exe --inprivate","relative.txt"})
            { bool rejected=false; try { App.LauncherTargets.Create("invalid",invalid,""); } catch(Exception ex) when(ex is ArgumentException or IOException) { rejected=true; } check(rejected,"Unsupported target rejected: "+invalid); }
            bool argsRejected=false; try { App.LauncherTargets.Create("file",document,"--x"); } catch(ArgumentException) { argsRejected=true; }
            check(argsRejected,"Separate arguments rejected for documents");
            var info=App.LauncherTargets.StartInfo(exe); check(info.UseShellExecute&&info.FileName==program&&info.Arguments=="--launcher-child","Program start preserves target and explicit arguments without command shell");
            check(App.LauncherTargets.StartInfo(web).FileName==web.Target&&App.LauncherTargets.StartInfo(web).Arguments=="","Website dispatched unchanged to default application");
            check(App.LauncherTargets.StartInfo(file,true).FileName==root&&App.LauncherTargets.StartInfo(file,true).Arguments=="","Open file location selects parent directory");
            using(var child=Process.Start(info))check(child!=null&&child.WaitForExit(10000)&&child.ExitCode==0,"Native shell launch runs isolated check child and exits");
            dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic link=shell.CreateShortcut(Path.Combine(root,"test shortcut.lnk"));
            try { link.TargetPath=program; link.Arguments="--launcher-child"; link.WorkingDirectory=AppContext.BaseDirectory; link.Save(); }
            finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
            var shortcut=App.LauncherTargets.Create("Link",Path.Combine(root,"test shortcut.lnk"),"");
            check(App.LauncherTargets.StartInfo(shortcut).FileName==shortcut.Target&&App.LauncherTargets.StartInfo(shortcut).Arguments=="","Native shortcut keeps .lnk and its embedded launch parameters");
            using(var child=Process.Start(App.LauncherTargets.StartInfo(shortcut)))check(child==null||child.WaitForExit(10000)&&child.ExitCode==0,"Native Windows shortcut opens isolated check child");
            foreach(var entry in new[]{exe,file,folder,shortcut,web}) { using var icon=App.LauncherIcons.Load(entry); check(icon.Width>0&&icon.Height>0,"Native icon retrieval: "+entry.Name); }
            using(var process=Process.GetCurrentProcess())
            {
                uint before=GetGuiResources(process.Handle,0);
                for(int i=0;i<100;i++) { using var icon=App.LauncherIcons.Load(exe); }
                uint after=GetGuiResources(process.Handle,0);
                check(before>0&&after<=before+2,$"100 native icon loads retain no growing GDI resources ({before} → {after})");
            }
            ShortcutCopies(root,shortcut,exe,file,folder,web,check);
            ShellDrops(root,shortcut,check);
            var entries=new List<App.LauncherEntry> { exe,file,folder,web,shortcut }; App.LauncherTargets.Move(entries,0,1); check(entries[1]==exe&&entries[0]==file,"Move down preserves entry identity");
            App.LauncherTargets.Move(entries,1,-1); check(entries[0]==exe,"Move up restores original order"); App.LauncherTargets.Move(entries,0,-1); check(entries[0]==exe,"Move beyond boundary is ignored");
            var settings=new App.WidgetSettings { Launcher=new() { Columns=3,ShowNames=false,Locked=true,Entries=entries.ToList(),Location=new(30,40) } }; settings.Save(registry);
            var loaded=App.WidgetSettings.Load(registry); check(JsonSerializer.Serialize(loaded.Launcher)==JsonSerializer.Serialize(settings.Launcher),"Launcher configuration persists order, arguments, layout and position");
            var clone=loaded.Clone(); clone.Launcher.Entries.Clear(); check(loaded.Launcher.Entries.Count==5,"Cloned launcher entry list does not alias");
            check(App.LauncherSettings.Parse("invalid").Entries.Count==0,"Corrupt launcher configuration resets safely");
            var normalized=App.LauncherSettings.Parse("{\"Columns\":99,\"MaximumHeight\":1,\"Style\":99,\"Entries\":null}");
            check(normalized.Columns==6&&normalized.MaximumHeight==300&&normalized.Style==App.SystemWidgetStyle.Glow&&normalized.Entries.Count==0,"Invalid limits, style and null list normalized");
            loaded.Launcher.Entries=Enumerable.Range(0,120).Select(i=>web with { Id=Guid.NewGuid() }).ToList(); check(App.LauncherSettings.Parse(JsonSerializer.Serialize(loaded.Launcher)).Entries.Count==100,"Stored history bounded to 100 launch entries");
            loaded.Launcher.Entries=entries;
            using(var editor=new App.WidgetSettingsEditor(loaded))
            {
                editor.SelectWidget("LauncherTitle"); check(editor.ReadWidgetSettings(false).Launcher.Entries.SequenceEqual(entries),"Settings editor displays launcher and preserves entry list");
                string? output=Environment.GetEnvironmentVariable("WALLPAPER_LAUNCHER_PREVIEW"); if(!string.IsNullOrEmpty(output)) { editor.ApplyPresentation(true,"de"); using var bitmap=new System.Drawing.Bitmap(editor.Width,editor.Height); editor.DrawToBitmap(bitmap,editor.ClientRectangle); bitmap.Save(Path.ChangeExtension(output,null)+"-settings.png"); }
            }
            using(var manager=new App.WidgetManager(()=>{},registryPath:registry,notesStore:new App.NotesStore(Path.Combine(root,"notes.json"))))
            {
                var baseline=manager.Settings; manager.SetEditing(true); manager.SaveLauncherEntries([web]); var preview=baseline.Clone(); preview.Launcher.Columns=6; manager.Preview(preview);
                check(manager.Settings.Launcher.Entries.SequenceEqual(new[]{web})&&App.WidgetSettings.Load(registry).Launcher.Columns==3,"Entry management persists independently while visual preview stays uncommitted");
                manager.CancelPreview(baseline); check(manager.Settings.Launcher.Entries.SequenceEqual(new[]{web})&&manager.Settings.Launcher.Columns==3,"Discard visual preview preserves explicitly saved launcher entries");
            }
            var data=new System.Windows.Forms.DataObject(); data.SetData(System.Windows.Forms.DataFormats.FileDrop,new[]{document,root,shortcut.Target});
            check(App.LauncherWidgetForm.DropTargets(data).SequenceEqual(new[]{document,root,shortcut.Target}),"File, folder and shortcut drops preserve full original targets");
            var urlData=new System.Windows.Forms.DataObject(); urlData.SetData(System.Windows.Forms.DataFormats.UnicodeText,"https://example.com/"); check(App.LauncherWidgetForm.DropTargets(urlData).Length==1,"HTTP URL text can be dropped");
            var badData=new System.Windows.Forms.DataObject(); badData.SetData(System.Windows.Forms.DataFormats.UnicodeText,"powershell -command test"); check(App.LauncherWidgetForm.DropTargets(badData).Length==0,"Dropped text cannot become arbitrary commands");
            var diagnosticData=new FormatOnlyData();
            check(App.LauncherWidgetForm.DropEffect(System.Windows.Forms.DragDropEffects.Link,true)==System.Windows.Forms.DragDropEffects.Link,"Start-menu Link-only source negotiates Link");
            check(App.LauncherWidgetForm.DropEffect(System.Windows.Forms.DragDropEffects.Move|System.Windows.Forms.DragDropEffects.Link,true)==System.Windows.Forms.DragDropEffects.Link,"Move/Link source negotiates Link without moving original");
            check(App.LauncherWidgetForm.DropEffect(System.Windows.Forms.DragDropEffects.Copy|System.Windows.Forms.DragDropEffects.Link,true)==System.Windows.Forms.DragDropEffects.Copy,"Copy remains preferred when offered");
            check(App.LauncherWidgetForm.DropEffect(System.Windows.Forms.DragDropEffects.Move,true)==System.Windows.Forms.DragDropEffects.None,"Move-only source is rejected to preserve original");
            check(App.LauncherWidgetForm.DropEffect(System.Windows.Forms.DragDropEffects.Link,false)==System.Windows.Forms.DragDropEffects.None,"Link without supported target data is rejected");
            var diagnostic=App.LauncherWidgetForm.DropFormatSummary(diagnosticData);
            check(diagnostic.Contains("Shell IDList Array")&&diagnostic.Contains("FileGroupDescriptorW")&&diagnosticData.ContentReads==0,"Drop diagnostics enumerate offered shell formats without reading payloads");
            check(diagnostic.Contains("otherFormatCount=1")&&!diagnostic.Contains("private-marker"),"Drop diagnostics redact custom format names and potential private information");
            diagnosticData.Fail=true; check(App.LauncherWidgetForm.DropFormatSummary(diagnosticData)=="formats=unavailable errorType=InvalidOperationException","Failed format enumeration logs exception type without its message");
            var widgetSettings=new App.LauncherSettings { Entries=entries,Locked=false };
            List<App.LauncherEntry>? menuResult=null;
            using(var menuWidget=new App.LauncherWidgetForm(widgetSettings,"de",_=>{},value=>menuResult=value,()=>{}))
            {
                var populate=typeof(App.LauncherWidgetForm).GetMethod("PopulateMenu",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var menu=(System.Windows.Forms.ContextMenuStrip)typeof(App.LauncherWidgetForm).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(menuWidget)!;
                populate.Invoke(menuWidget,[web]);
                menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(item=>item.Text==App.Localization.Get("LauncherDelete","de")).PerformClick();
                check(menuResult!=null&&menuResult.SequenceEqual(entries.Where(e=>e.Id!=web.Id)),"Context-menu delete saves only removal of selected launcher entry");
                populate.Invoke(menuWidget,[null]);
                check(!menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(item=>item.Text==App.Localization.Get("LauncherDelete","de")),"Launcher header menu has no entry-specific delete action");
            }
            using(var widget=new App.LauncherWidgetForm(widgetSettings,"de",_=>{},_=>{},()=>{}))
            {
                check(widget.CanDrop(data),"Unlocked launcher accepts add drops"); widgetSettings.Locked=true; widget.Apply(widgetSettings,"de"); check(widget.CanDrop(data)&&widget.AllowDrop,"Position-locked launcher still accepts add drops");
                widgetSettings.Locked=false; widget.Apply(widgetSettings,"de"); widget.SetActivitySuspended(true); check(!widget.CanDrop(data),"Activity suspension blocks drop interactions"); widget.SetActivitySuspended(false);
                foreach(var style in Enum.GetValues<App.SystemWidgetStyle>())foreach(var names in new[]{true,false})foreach(int columns in new[]{2,4,6})foreach(int dpi in new[]{96,192})
                {
                    widgetSettings.Style=style; widgetSettings.ShowNames=names; widgetSettings.Columns=columns; widgetSettings.Entries=Enumerable.Range(0,100).Select(i=>web with { Id=Guid.NewGuid(),Name="Entry "+i }).ToList();
                    widget.Apply(widgetSettings,"de"); widget.ApplyTheme(names); using var bitmap=widget.RenderBitmap(2000,dpi); check(bitmap.Width==(columns*(names?99:76)+44)*dpi/96&&bitmap.Height<=500*dpi/96,$"Grid bounds {style}, names={names}, columns={columns}, dpi={dpi}");
                }
                var viewport=(App.CalendarViewport)typeof(App.LauncherWidgetForm).GetField("viewport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget)!;
                check(viewport.CanScroll&&viewport.Wheel(-120,3)&&viewport.ScrollOffset>0,"Large launcher grid scrolls within height cap"); check(viewport.DragThumb(viewport.Track.Bottom,0)&&viewport.ScrollOffset==viewport.MaxScrollOffset,"Launcher scrollbar thumb reaches final entries");
                string? output=Environment.GetEnvironmentVariable("WALLPAPER_LAUNCHER_PREVIEW"); if(!string.IsNullOrEmpty(output))
                {
                    widgetSettings.Columns=4; widgetSettings.ShowNames=true; widgetSettings.Entries=[exe with { Name="WallpaperControl" },folder with { Name="Bilder" },web,file with { Name="Notizen" },shortcut with { Name="Verknüpfung" }];
                    _=widget.Handle; widget.ApplyTheme(true); widget.Apply(widgetSettings,"de"); var until=DateTime.UtcNow.AddSeconds(2); while(DateTime.UtcNow<until) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); }
                    using var bitmap=widget.RenderBitmap(); bitmap.Save(output);
                }
            }
            System.IO.File.Delete(document); bool missing=false; try { App.LauncherTargets.StartInfo(file); } catch(FileNotFoundException) { missing=true; } check(missing,"Deleted target fails safely without removing saved entry");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry,false); Directory.Delete(root,true); }
    }
    private static void ShortcutCopies(string root,App.LauncherEntry shortcut,App.LauncherEntry exe,App.LauncherEntry file,App.LauncherEntry folder,App.LauncherEntry web,Action<bool,string> check)
    {
        var directory=Path.Combine(root,"managed"); var store=new App.LauncherShortcutStore(directory);
        var desktop=Path.Combine(root,"desktop shortcut.lnk"); System.IO.File.Copy(shortcut.Target,desktop);
        var source=shortcut with { Target=desktop };
        var imported=store.Update([], [source,exe,file,folder,web],_=>true)!; var owned=imported[0];
        check(owned.ManagedShortcut&&store.Owns(owned)&&owned.Id==source.Id&&owned.Name==source.Name&&owned.Target!=source.Target,"Shortcut import keeps identity and stores an owned copy");
        check(System.IO.File.Exists(desktop)&&System.IO.File.ReadAllBytes(desktop).SequenceEqual(System.IO.File.ReadAllBytes(owned.Target)),"Shortcut import preserves original and copies exact .lnk bytes");
        check(imported.Skip(1).SequenceEqual(new[]{exe,file,folder,web}),"Programs, files, folders and URLs are never copied");
        System.IO.File.Delete(desktop);
        using(var child=Process.Start(App.LauncherTargets.StartInfo(owned)))check(child==null||child.WaitForExit(10000)&&child.ExitCode==0,"Native managed shortcut still starts after desktop original deletion");
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!; dynamic link=shell.CreateShortcut(owned.Target);
        try { check(string.Equals((string)link.TargetPath,exe.Target,StringComparison.OrdinalIgnoreCase)&&(string)link.Arguments=="--launcher-child"&&string.Equals(((string)link.WorkingDirectory).TrimEnd('\\'),AppContext.BaseDirectory.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase),"Native managed .lnk retains target, arguments and working directory"); }
        finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
        var repeated=store.Update(imported,imported,_=>true)!; check(repeated[0].Target==owned.Target&&Directory.GetFiles(directory).Length==1,"Reapply and reorder reuse managed shortcuts without accumulating copies");
        var renamed=store.Update(imported,[owned with { Name="Renamed" }],_=>true)!; check(renamed[0].Target==owned.Target,"Renaming an owned entry keeps its copy");
        var shared=owned with { Id=Guid.NewGuid() }; var remaining=store.Update([owned,shared],[shared],_=>true)!; check(System.IO.File.Exists(owned.Target),"Shared shortcut remains until last referencing entry is removed");
        var before=Directory.GetFiles(directory).Order().ToArray(); var failed=store.Update(remaining,[shortcut],_=>false);
        check(failed==null&&Directory.GetFiles(directory).Order().SequenceEqual(before),"Failed persistence rolls back new copy and preserves previous resources");
        bool batchFailed=false; try { store.Update(remaining,[shortcut,shortcut with { Id=Guid.NewGuid(),Target=Path.Combine(root,"unavailable.lnk") }],_=>true); } catch(IOException) { batchFailed=true; }
        check(batchFailed&&Directory.GetFiles(directory).Order().SequenceEqual(before),"Failure midway through batch rolls back every newly created shortcut");
        bool locked=false;
        using(var lockSource=new FileStream(shortcut.Target,FileMode.Open,FileAccess.Read,FileShare.None))
        { try { store.Update(remaining,[shortcut],_=>true); } catch(IOException) { locked=true; } }
        check(locked&&Directory.GetFiles(directory).Order().SequenceEqual(before),"Locked shortcut import fails without destroying previous copy");
        var keep=Path.Combine(directory,Guid.NewGuid().ToString("N")+".lnk"); System.IO.File.Copy(shortcut.Target,keep);
        var unowned=shortcut with { Target=keep,ManagedShortcut=false }; var outside=shortcut with { ManagedShortcut=true };
        store.Update([shared,unowned,outside],[],_=>true);
        check(!System.IO.File.Exists(owned.Target)&&System.IO.File.Exists(keep)&&System.IO.File.Exists(shortcut.Target),"Delete removes owned copy only, never unowned files or outside originals");
        var siblings=store.Update([], [shortcut,shortcut with { Id=Guid.NewGuid() }],_=>true)!;
        check(siblings[0].Target!=siblings[1].Target&&siblings.All(store.Owns),"Identical shortcut names receive collision-free owned filenames");
        var replacement=store.Update(siblings,[web with { Id=shortcut.Id }],_=>true)!;
        check(siblings.All(e=>!System.IO.File.Exists(e.Target))&&replacement[0]==web with { Id=shortcut.Id },"Replacing shortcuts with URL cleans obsolete owned copies");
        var missing=shortcut with { Target=Path.Combine(root,"missing.lnk") };
        check(store.Update([missing],[missing],_=>true,tolerateMissing:true)![0]==missing,"Migration retains missing legacy entry without fabricating a copy");
        var linkFolder=Path.Combine(root,"folder.lnk"); Directory.CreateDirectory(linkFolder); var folderEntry=folder with { Target=linkFolder };
        check(store.Update([],[folderEntry],_=>true)![0]==folderEntry,"Folder named .lnk remains an ordinary folder");
        string registry=@"Software\WallpaperControl.ShortcutMigrationChecks-"+Guid.NewGuid().ToString("N");
        try
        {
            var initial=new App.WidgetSettings { Launcher=new() { Entries=[shortcut] } }; initial.Save(registry);
            using var manager=new App.WidgetManager(()=>{},registryPath:registry,notesStore:new App.NotesStore(Path.Combine(root,"migration-notes.json")));
            var field=typeof(App.WidgetManager).GetField("launcherShortcuts",BindingFlags.Instance|BindingFlags.NonPublic)!; field.SetValue(manager,new App.LauncherShortcutStore(Path.Combine(root,"migration")));
            manager.Start(); var migrated=manager.Settings.Launcher.Entries[0];
            check(migrated.ManagedShortcut&&migrated.Target!=shortcut.Target&&App.WidgetSettings.Load(registry).Launcher.Entries[0]==migrated,"Startup migrates existing shortcut even while widget is disabled");
            manager.SaveLauncherEntries([]); check(!System.IO.File.Exists(migrated.Target)&&System.IO.File.Exists(shortcut.Target),"Manager deletion cleans managed migration copy and retains original");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry,false); }
    }
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process,uint flags);
    private static void ShellDrops(string root,App.LauncherEntry shortcut,Action<bool,string> check)
    {
        check(App.LauncherShellDrop.Parse([1,0,0,0]).Count==0,"Truncated shell drop is rejected");
        var malformed=new byte[18]; BitConverter.GetBytes(1u).CopyTo(malformed,0); BitConverter.GetBytes(12u).CopyTo(malformed,4); BitConverter.GetBytes(14u).CopyTo(malformed,8); BitConverter.GetBytes((ushort)65535).CopyTo(malformed,14);
        check(App.LauncherShellDrop.Parse(malformed).Count==0,"Out-of-bounds PIDL is rejected before native access");
        foreach(var target in new[]{shortcut.Target,"shell:AppsFolder"})
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(target,IntPtr.Zero,out var item,0,out _));
            try
            {
                int size=(int)ILGetSize(item); var bytes=new byte[14+size]; BitConverter.GetBytes(1u).CopyTo(bytes,0); BitConverter.GetBytes(12u).CopyTo(bytes,4); BitConverter.GetBytes(14u).CopyTo(bytes,8); Marshal.Copy(item,bytes,14,size);
                var data=new System.Windows.Forms.DataObject(); data.SetData("Shell IDList Array",false,new MemoryStream(bytes));
                var directory=Path.Combine(root,"shell-staging"); var resolved=App.LauncherShellDrop.Materialize(data,directory);
                if(target==shortcut.Target)check(resolved.Count==1&&resolved[0]==shortcut.Target,"Native shell drop resolves filesystem shortcut without replacing it");
                else
                {
                    check(resolved.Count==1&&System.IO.File.Exists(resolved[0])&&Path.GetExtension(resolved[0])==".lnk","Native virtual shell item becomes a real Windows shortcut");
                    var store=new App.LauncherShortcutStore(Path.Combine(root,"shell-managed")); var entries=store.Update([],[App.LauncherTargets.Create("Apps",resolved[0],"")],_=>true)!;
                    System.IO.File.Delete(resolved[0]); check(System.IO.File.Exists(entries[0].Target)&&entries[0].ManagedShortcut,"Virtual shell shortcut survives staging cleanup in managed storage");
                    store.Update(entries,[],_=>true);
                }
            }
            finally { Marshal.FreeCoTaskMem(item); }
        }
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHParseDisplayName(string name,IntPtr context,out IntPtr item,uint attributes,out uint result);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr item);
    private sealed class FormatOnlyData : System.Windows.Forms.DataObject
    {
        internal int ContentReads;
        internal bool Fail;
        public override string[] GetFormats(bool autoConvert) { if(Fail)throw new InvalidOperationException("private-marker"); return ["Shell IDList Array","FileGroupDescriptorW","private-marker"]; }
        [Obsolete("Legacy payload access is deliberately intercepted to detect accidental reads in this diagnostic test.")]
        public override object? GetData(string format,bool autoConvert) { ContentReads++; throw new InvalidOperationException(); }
    }
}
