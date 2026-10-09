namespace WallpaperControl;

/// <summary>Owns only GUID-named shortcut copies in one dedicated directory.</summary>
internal sealed class LauncherShortcutStore
{
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WallpaperControl","Launcher","Shortcuts");
    private readonly string directory;
    internal LauncherShortcutStore(string directory) => this.directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
    internal bool Owns(LauncherEntry entry)
    {
        if(!entry.ManagedShortcut||LauncherTargets.IsWebsite(entry.Target))return false;
        try
        {
            var path=Path.GetFullPath(entry.Target);
            return string.Equals(Path.GetDirectoryName(path),directory,StringComparison.OrdinalIgnoreCase)
                &&Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase)
                &&Guid.TryParseExact(Path.GetFileNameWithoutExtension(path),"N",out _);
        }
        catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException) { return false; }
    }
    private void VerifyDirectory()
    {
        // Never copy into or delete through a redirected directory/junction.
        for(var current=new DirectoryInfo(directory); current!=null; current=current.Parent)
            if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected shortcut storage directory.");
    }
    internal List<LauncherEntry>? Update(IReadOnlyList<LauncherEntry> previous,IReadOnlyList<LauncherEntry> requested,Func<List<LauncherEntry>,bool> persist,bool tolerateMissing=false)
    {
        var created=new List<LauncherEntry>(); var next=new List<LauncherEntry>(); bool committed=false;
        try
        {
            foreach(var entry in requested.Take(LauncherSettings.MaximumEntries))
            {
                if(LauncherTargets.IsWebsite(entry.Target)||!Path.GetExtension(entry.Target).Equals(".lnk",StringComparison.OrdinalIgnoreCase)||Directory.Exists(entry.Target)) { next.Add(entry with { ManagedShortcut=false }); continue; }
                if(Owns(entry)) { next.Add(entry); continue; }
                if(!System.IO.File.Exists(entry.Target)&&(tolerateMissing||previous.Any(old=>old.Target==entry.Target))) { next.Add(entry with { ManagedShortcut=false }); continue; }
                VerifyDirectory(); Directory.CreateDirectory(directory); VerifyDirectory();
                var copy=entry with { Target=Path.Combine(directory,Guid.NewGuid().ToString("N")+".lnk"),ManagedShortcut=true };
                // Register before copying so even a partial copy is removed on failure.
                created.Add(copy); System.IO.File.Copy(entry.Target,copy.Target,false); next.Add(copy);
            }
            if(!persist(next))return null;
            committed=true;
            foreach(var old in previous.Where(Owns).DistinctBy(e=>e.Target,StringComparer.OrdinalIgnoreCase))
                if(!next.Any(e=>string.Equals(e.Target,old.Target,StringComparison.OrdinalIgnoreCase)))Remove(old);
            return next;
        }
        finally { if(!committed)foreach(var copy in created)Remove(copy); }
    }
    private void Remove(LauncherEntry entry)
    {
        if(!Owns(entry))return;
        try
        {
            VerifyDirectory();
            var path=Path.GetFullPath(entry.Target);
            if(System.IO.File.Exists(path)&&(System.IO.File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return;
            System.IO.File.Delete(path);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { AppLogger.Info($"Launcher shortcut cleanup failed: type={ex.GetType().Name}"); }
    }
}
