using Microsoft.Win32;
using System.Text.Json;

namespace WallpaperControl;

internal enum VisitorSourceKind { Image, Video, Folder }

internal sealed record VisitorModeSettings
{
    public bool Active { get; init; }
    public VisitorSourceKind Source { get; init; }
    public string Path { get; init; } = "";
    public uint IntervalMilliseconds { get; init; } = 300000;
    public bool Shuffle { get; init; }
    public bool WidgetEnabled { get; init; }
    public bool WidgetLocked { get; init; }
    public int WidgetX { get; init; } = 40;
    public int WidgetY { get; init; } = 140;
    public uint HotkeyModifiers { get; init; }
    public uint HotkeyKey { get; init; }
    public VisitorResumeState? Resume { get; init; }
}

internal sealed record VisitorResumeState
{
    public bool Video { get; init; }
    public string VideoPath { get; init; } = "";
    public long VideoPosition { get; init; }
    public bool VideoPaused { get; init; }
    public string? ImagePath { get; init; }
    public Dictionary<string, string> MonitorImages { get; init; } = new();
    public bool Custom { get; init; }
    public bool NativeActive { get; init; }
    public bool ImagePaused { get; init; }
    public long RemainingTicks { get; init; }
    public uint Interval { get; init; }
    public bool Shuffle { get; init; }
}

// A single registry value commits the active flag and its resume snapshot together.
// All calls (including events) run on the owning UI context; the gate covers awaits.
internal sealed class VisitorModeStore(string registryPath = @"Software\WallpaperControl")
{
    internal VisitorModeSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            var settings = JsonSerializer.Deserialize<VisitorModeSettings>(key?.GetValue("VisitorMode") as string ?? "{}");
            return settings == null ? new() : settings with
            {
                Path = settings.Path ?? "",
                IntervalMilliseconds = Math.Clamp(settings.IntervalMilliseconds, 1000u, 86400000u)
            };
        }
        catch (Exception ex) { AppLogger.Warning("Could not load visitor mode settings.", ex); return new(); }
    }
    internal bool Save(VisitorModeSettings settings)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(registryPath);
            key.SetValue("VisitorMode", JsonSerializer.Serialize(settings), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) { SettingsPersistence.ReportFailure("Could not save visitor mode settings.", ex); return false; }
    }
}

internal sealed class VisitorModeService
{
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly Func<VisitorModeSettings, bool> save;
    private readonly Func<VisitorResumeState> capture;
    private readonly Func<Task> activate;
    private readonly Func<VisitorResumeState?, Task> restore;
    private bool closed;
    internal VisitorModeSettings Settings { get; private set; }
    internal bool Active => Settings.Active;
    internal bool Busy { get; private set; }
    internal string? Error { get; private set; }
    internal event Action? Changed;

    internal VisitorModeService(VisitorModeSettings settings, Func<VisitorModeSettings, bool> save,
        Func<VisitorResumeState> capture, Func<Task> activate, Func<VisitorResumeState?, Task> restore)
    { Settings = settings; this.save = save; this.capture = capture; this.activate = activate; this.restore = restore; }

    internal Task ToggleAsync() => ChangeAsync(null, false);
    internal Task SetActiveAsync(bool active, bool startup = false) => ChangeAsync(active, startup);
    private async Task ChangeAsync(bool? requested, bool startup)
    {
        await changes.WaitAsync();
        try
        {
            bool active = requested ?? !Active;
            if (closed || (!startup && Active == active)) return;
            Busy = true; Error = null;
            if (active)
            {
                // Capture before reserving the visitor desktop; persisted startup already has a snapshot.
                Settings = Settings with { Active = true, Resume = startup ? Settings.Resume : capture() };
                if (!save(Settings)) Error = "VisitorSaveError";
                Changed?.Invoke();
                await activate();
            }
            else
            {
                // Keep Active true until restoration finishes, blocking normal user actions meanwhile.
                await restore(Settings.Resume);
                Settings = Settings with { Active = false, Resume = null };
                if (!save(Settings)) Error = "VisitorSaveError";
            }
        }
        catch (Exception ex)
        { AppLogger.Warning("Visitor mode change failed.", ex); Error = "VisitorPlaybackError"; }
        finally { Busy = false; changes.Release(); if (!closed) Changed?.Invoke(); }
    }

    internal async Task ConfigureAsync(Func<VisitorModeSettings, VisitorModeSettings> update, bool reloadMedia = true)
    {
        await changes.WaitAsync();
        try
        {
            if (closed) return;
            Busy = true; Error = null;
            var updated = update(Settings);
            Settings = updated with { Active = Active, Resume = Settings.Resume,
                IntervalMilliseconds = Math.Clamp(updated.IntervalMilliseconds, 1000u, 86400000u) };
            if (!save(Settings)) Error = "VisitorSaveError";
            Changed?.Invoke();
            if (Active && reloadMedia) await activate();
        }
        catch (Exception ex) { AppLogger.Warning("Visitor configuration failed.", ex); Error = "VisitorPlaybackError"; }
        finally { Busy = false; changes.Release(); if (!closed) Changed?.Invoke(); }
    }
    internal void ReportMediaError() { Error = "VisitorMediaError"; Changed?.Invoke(); }
    internal void Close() { closed = true; Changed = null; }
    internal async Task DrainAsync()
    { Close(); await changes.WaitAsync(); changes.Release(); }
}
