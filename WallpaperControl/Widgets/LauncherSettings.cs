using System.Diagnostics;
using System.Text.Json;
namespace WallpaperControl;

internal enum LauncherTargetKind { Program, Folder, File, Website }
internal sealed record LauncherEntry(Guid Id, string Name, string Target, string Arguments = "", bool ManagedShortcut = false);
internal sealed class LauncherSettings
{
    internal const int MaximumEntries = 100;
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public SystemWidgetStyle Style { get; set; } = SystemWidgetStyle.Glow;
    public bool ShowNames { get; set; } = true;
    public int Columns { get; set; } = 4;
    public int MaximumHeight { get; set; } = 500;
    public Point Location { get; set; } = new(700, 500);
    public List<LauncherEntry> Entries { get; set; } = [];
    internal LauncherSettings Clone() => new() { Enabled=Enabled, Locked=Locked, Style=Style, ShowNames=ShowNames, Columns=Columns, MaximumHeight=MaximumHeight, Location=Location, Entries=Entries.ToList() };
    internal static LauncherSettings Parse(string json)
    {
        try
        {
            var value=JsonSerializer.Deserialize<LauncherSettings>(json) ?? new();
            value.Columns=Math.Clamp(value.Columns,2,6); value.MaximumHeight=CalendarViewport.NormalizeMaximum(value.MaximumHeight);
            if(!Enum.IsDefined(value.Style))value.Style=SystemWidgetStyle.Glow;
            value.Entries=(value.Entries ?? []).Where(e=>e!=null && e.Id!=Guid.Empty && !string.IsNullOrWhiteSpace(e.Name) && !string.IsNullOrWhiteSpace(e.Target))
                .DistinctBy(e=>e.Id).Take(MaximumEntries).ToList();
            return value;
        }
        catch(JsonException) { return new(); }
    }
}

internal static class LauncherTargets
{
    internal static string Resolve(string target)
    {
        target=target.Trim();
        if(IsWebsite(target))return target;
        target=Environment.ExpandEnvironmentVariables(target.Trim('"'));
        if(Path.IsPathFullyQualified(target))return Path.GetFullPath(target);
        // Only bare executable names may be resolved through PATH, never command lines.
        if(Path.GetFileName(target)==target && Path.GetExtension(target).Equals(".exe",StringComparison.OrdinalIgnoreCase))
            foreach(var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { var candidate=Path.Combine(directory.Trim('"'),target); if(Path.IsPathFullyQualified(candidate)&&System.IO.File.Exists(candidate))return Path.GetFullPath(candidate); }
                catch(ArgumentException) { }
            }
        throw new ArgumentException("LauncherInvalidTarget");
    }
    internal static bool IsWebsite(string target) => Uri.TryCreate(target,UriKind.Absolute,out var uri) && (uri.Scheme==Uri.UriSchemeHttp||uri.Scheme==Uri.UriSchemeHttps) && !string.IsNullOrEmpty(uri.Host);
    internal static LauncherTargetKind Kind(string target) => IsWebsite(target)?LauncherTargetKind.Website:Directory.Exists(target)?LauncherTargetKind.Folder:
        Path.GetExtension(target).Equals(".exe",StringComparison.OrdinalIgnoreCase)?LauncherTargetKind.Program:LauncherTargetKind.File;
    internal static LauncherEntry Create(string name,string target,string arguments,Guid? id=null)
    {
        var resolved=Resolve(target); var kind=Kind(resolved);
        if(kind!=LauncherTargetKind.Website && !Directory.Exists(resolved)&&!System.IO.File.Exists(resolved))throw new FileNotFoundException();
        if(!string.IsNullOrWhiteSpace(arguments)&&kind!=LauncherTargetKind.Program)throw new ArgumentException("LauncherArgumentsOnly");
        if(string.IsNullOrWhiteSpace(name)) name=kind==LauncherTargetKind.Website?new Uri(resolved).Host:Path.GetFileNameWithoutExtension(resolved.TrimEnd('\\'));
        if(string.IsNullOrWhiteSpace(name))name=resolved;
        return new(id??Guid.NewGuid(),name.Trim(),resolved,arguments);
    }
    internal static ProcessStartInfo StartInfo(LauncherEntry entry,bool parent=false)
    {
        var target=Resolve(entry.Target); var kind=Kind(target);
        if(kind!=LauncherTargetKind.Website && !Directory.Exists(target)&&!System.IO.File.Exists(target))throw new FileNotFoundException();
        if(parent) { if(kind is LauncherTargetKind.Website or LauncherTargetKind.Folder)throw new ArgumentException(); target=Path.GetDirectoryName(target) ?? throw new ArgumentException(); }
        return new(target) { UseShellExecute=true, Arguments=!parent&&kind==LauncherTargetKind.Program?entry.Arguments:"" };
    }
    internal static void Start(LauncherEntry entry,bool parent=false) { using var process=Process.Start(StartInfo(entry,parent)); }
    internal static void Move(List<LauncherEntry> entries,int index,int direction)
    { int next=index+direction; if(index<0||index>=entries.Count||next<0||next>=entries.Count)return; (entries[index],entries[next])=(entries[next],entries[index]); }
}
