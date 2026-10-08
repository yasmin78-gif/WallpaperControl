using Windows.Media.Control;
using Windows.Storage.Streams;
namespace WallpaperControl;
/// <summary>Observes Windows sessions. Suspension never issues transport commands.</summary>
internal sealed class NowPlayingService : INowPlayingService
{
    // Windows Media Player can publish uncompressed BMP artwork larger than 2 MiB.
    private const uint MaximumCoverBytes = 8 * 1024 * 1024;
    private readonly SynchronizationContext context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
    private readonly CancellationTokenSource lifetime = new();
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? current;
    private GlobalSystemMediaTransportControlsSessionMediaProperties? cachedMedia;
    private byte[]? cachedCover;
    private int mediaRevision, cachedMediaRevision = -1;
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> ids = [];
    private string? currentId;
    private bool replaceSession;
    private bool disposed, suspended, refreshing, dirty, commandPending;
    private int revision;
    public event Action? Changed;
    public NowPlayingState State { get; private set; } = new();
    public IReadOnlyList<MediaSessionChoice> Sessions { get; private set; } = [];
    public string? Selected { get; private set; }
    internal Task Initialization { get; }
    internal NowPlayingService() { Initialization = InitializeAsync(); }
    private async Task InitializeAsync()
    {
        try
        {
            var result = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(lifetime.Token);
            if (disposed) return;
            manager = result; manager.SessionsChanged += SessionsChanged; manager.CurrentSessionChanged += CurrentChanged; RequestRefresh();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!disposed) { AppLogger.Info($"Now Playing initialization failed: {ex.Message}"); State = new() { Unavailable = true }; Changed?.Invoke(); } }
    }
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => context.Post(_ => { if (!disposed) { replaceSession = true; RequestRefresh(); } },null);
    private void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => context.Post(_ => { if (!disposed) { if (Selected == null) replaceSession = true; RequestRefresh(); } },null);
    private void MediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => context.Post(_ => { if (!disposed) { mediaRevision++; RequestRefresh(); } },null);
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => PostRefresh();
    private void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => PostRefresh();
    private void PostRefresh() => context.Post(_ => { if (!disposed) RequestRefresh(); }, null);
    public void Select(string? id) { if (Selected == id || disposed) return; Selected = id; revision++; RequestRefresh(); }
    public void SetSuspended(bool value) { if (suspended == value || disposed) return; suspended = value; revision++; if (!value) RequestRefresh(); }
    private void Subscribe(GlobalSystemMediaTransportControlsSession? value)
    {
        if (ReferenceEquals(current, value)) return;
        if (current != null) { current.MediaPropertiesChanged -= MediaChanged; current.PlaybackInfoChanged -= PlaybackChanged; current.TimelinePropertiesChanged -= TimelineChanged; }
        current = value;
        cachedMedia = null; cachedCover = null; cachedMediaRevision = -1;
        if (current != null) { current.MediaPropertiesChanged += MediaChanged; current.PlaybackInfoChanged += PlaybackChanged; current.TimelinePropertiesChanged += TimelineChanged; }
    }
    internal void RequestRefresh() { if (disposed || suspended || manager == null) return; revision++; dirty = true; if (!refreshing) _ = RefreshAsync(); }
    private async Task RefreshAsync()
    {
        refreshing = true;
        try
        {
            while (dirty && !disposed && !suspended)
            {
                dirty = false; int generation = revision;
                try
                {
                    var available = manager!.GetSessions().ToArray();
                    // GSMTC creates fresh native wrapper objects for the same logical session.
                    // App IDs are stable; duplicate app IDs get an occurrence suffix in the menu.
                    ids.Clear(); var counts = new Dictionary<string,int>();
                    foreach (var session in available)
                    {
                        string source = session.SourceAppUserModelId;
                        int occurrence = counts.GetValueOrDefault(source); counts[source] = occurrence+1;
                        ids.Add(source+"\u001f"+occurrence,session);
                    }
                    Sessions = ids.Select(entry => new MediaSessionChoice(entry.Key,SourceName(entry.Value.SourceAppUserModelId)
                        + (counts[entry.Value.SourceAppUserModelId] > 1 ? " ("+(int.Parse(entry.Key.Split('\u001f').Last())+1)+")" : ""))).ToArray();
                    var automatic = Selected == null ? manager.GetCurrentSession() : null;
                    string? selectedId = Selected ?? (automatic == null ? null : ids.FirstOrDefault(entry => entry.Value.SourceAppUserModelId == automatic.SourceAppUserModelId).Key);
                    var candidate = Selected == null ? automatic : ids.GetValueOrDefault(Selected);
                    if (replaceSession || currentId != selectedId || current == null || candidate == null) { Subscribe(candidate); currentId = selectedId; replaceSession = false; }
                    var selected = current;
                    if (selected == null) { State = new(); Changed?.Invoke(); continue; }
                    int metadataGeneration = mediaRevision;
                    var media = cachedMediaRevision == metadataGeneration && cachedMedia != null ? cachedMedia : await selected.TryGetMediaPropertiesAsync().AsTask(lifetime.Token);
                    byte[]? cover = ReferenceEquals(media,cachedMedia) ? cachedCover : null;
                    if (!ReferenceEquals(media,cachedMedia) && media.Thumbnail != null)
                    {
                        try
                        {
                            using var stream = await media.Thumbnail.OpenReadAsync().AsTask(lifetime.Token);
                            if (stream.Size is > 0 and <= MaximumCoverBytes)
                            {
                                using var reader = new DataReader(stream);
                                uint length = await reader.LoadAsync((uint)stream.Size).AsTask(lifetime.Token);
                                cover = new byte[length]; reader.ReadBytes(cover);
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { AppLogger.Info($"Now Playing cover unavailable: {ex.Message}"); }
                    }
                    if (disposed || suspended || generation != revision) continue;
                    cachedMedia = media; cachedCover = cover; cachedMediaRevision = metadataGeneration;
                    var playback = selected.GetPlaybackInfo(); var timeline = selected.GetTimelineProperties(); var controls = playback.Controls;
                    State = new()
                    {
                        SessionId = selectedId, Source = selected.SourceAppUserModelId,
                        Title = media.Title ?? "", Artist = media.Artist ?? "", Album = media.AlbumTitle ?? "", Cover = cover,
                        Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        Previous = controls.IsPreviousEnabled, Next = controls.IsNextEnabled,
                        Toggle = controls.IsPlayPauseToggleEnabled || (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? controls.IsPauseEnabled : controls.IsPlayEnabled),
                        Seek = controls.IsPlaybackPositionEnabled && timeline.EndTime > timeline.StartTime && timeline.MaxSeekTime > timeline.MinSeekTime,
                        Start = timeline.StartTime, End = timeline.EndTime, MinimumSeek = timeline.MinSeekTime, MaximumSeek = timeline.MaxSeekTime,
                        Position = timeline.Position, Updated = timeline.LastUpdatedTime,
                        Rate = playback.PlaybackRate is double rate && double.IsFinite(rate) && rate >= 0 ? rate : 1
                    };
                    Changed?.Invoke();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { if (!disposed && !suspended && generation == revision) { AppLogger.Info($"Now Playing refresh failed: {ex.Message}"); State = new() { Unavailable = true }; Changed?.Invoke(); } }
            }
        }
        finally { refreshing = false; }
    }
    public async Task<bool> CommandAsync(MediaCommand command, TimeSpan position = default)
    {
        if (disposed || suspended || current == null || refreshing || commandPending) return false;
        var session = current; var state = State;
        commandPending = true;
        try
        {
            bool success = command switch
            {
                MediaCommand.Previous when state.Previous => await session.TrySkipPreviousAsync().AsTask(lifetime.Token),
                MediaCommand.Next when state.Next => await session.TrySkipNextAsync().AsTask(lifetime.Token),
                MediaCommand.Toggle when state.Toggle => session.GetPlaybackInfo().Controls.IsPlayPauseToggleEnabled
                    ? await session.TryTogglePlayPauseAsync().AsTask(lifetime.Token)
                    : state.Playing ? await session.TryPauseAsync().AsTask(lifetime.Token) : await session.TryPlayAsync().AsTask(lifetime.Token),
                MediaCommand.Seek when state.Seek => await session.TryChangePlaybackPositionAsync(Math.Clamp(position.Ticks, state.MinimumSeek.Ticks, Math.Max(state.MinimumSeek.Ticks, state.MaximumSeek.Ticks))).AsTask(lifetime.Token),
                _ => false
            };
            if (!disposed) RequestRefresh(); return success;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { AppLogger.Info($"Now Playing command {command} failed: {ex.Message}"); return false; }
        finally { commandPending = false; }
    }
    internal static string SourceName(string source, string language = "en")
    {
        if (source.StartsWith("Microsoft.ZuneMusic_",StringComparison.OrdinalIgnoreCase)) return Localization.Get("NowPlayingMediaPlayer",language);
        if (source.StartsWith("AmazonMobileLLC.AmazonMusic_",StringComparison.OrdinalIgnoreCase)) return "Amazon Music";
        if (source.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(source);
        // Chromium publishes IDs such as Vivaldi.<profile hash>, rather than an executable name.
        int dot = source.LastIndexOf('.');
        if (dot > 0 && source.Length-dot-1 >= 16 && source[(dot+1)..].All(char.IsAsciiLetterOrDigit)) return source[..dot];
        return source;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; revision++; lifetime.Cancel();
        if (manager != null) { manager.SessionsChanged -= SessionsChanged; manager.CurrentSessionChanged -= CurrentChanged; }
        Subscribe(null); currentId = null; cachedMedia = null; cachedCover = null; manager = null; ids.Clear(); Sessions = []; State = new(); Changed = null; lifetime.Dispose();
    }
}
