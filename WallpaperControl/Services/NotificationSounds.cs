using System.Media;

namespace WallpaperControl;

internal enum NotificationSoundKind { Off, WindowsStandard, Chime, Soft, Bell, Triad }

/// <summary>One cached, embedded WAV per owner; asynchronous playback never blocks the UI.</summary>
internal sealed class NotificationSounds : IDisposable
{
    private Stream? stream;
    private SoundPlayer? player;
    private bool disposed;
    private string? loadedResource;
    internal static NotificationSoundKind Normalize(int value) => Enum.IsDefined(typeof(NotificationSoundKind), value)
        ? (NotificationSoundKind)value : NotificationSoundKind.Chime;
    internal void Play(NotificationSoundKind sound)
    {
        if (disposed || sound == NotificationSoundKind.Off) return;
        try
        {
            if (sound == NotificationSoundKind.WindowsStandard) { SystemSounds.Asterisk.Play(); return; }
            string asset = sound switch { NotificationSoundKind.Soft => "Soft", NotificationSoundKind.Bell => "Bell", NotificationSoundKind.Triad => "Triad", _ => "Chime" };
            PlayResource("WallpaperControl.Notification" + asset + ".wav");
        }
        catch (Exception ex) { AppLogger.Info($"Notification sound unavailable: {ex.GetType().Name}."); }
    }
    internal void PlayEmail(EmailWatcherSound sound)
    {
        if (disposed || EmailWatcherSoundResources.ResourceName(sound) is not string resource) return;
        try { PlayResource(resource); } catch (Exception ex) { AppLogger.Info($"Notification sound unavailable: {ex.GetType().Name}."); }
    }
    private void PlayResource(string resource)
    {
            if (player == null || loadedResource != resource)
            {
                player?.Stop(); player?.Dispose(); stream?.Dispose(); player = null; stream = null; loadedResource=null;
                stream = typeof(NotificationSounds).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException("Notification sound resource missing.");
                player = new SoundPlayer(stream); player.Load(); loadedResource = resource;
            }
            player.Play();
    }
    public void Dispose() { if (disposed) return; disposed = true; player?.Stop(); player?.Dispose(); stream?.Dispose(); }
}
