using System.Media;

namespace WallpaperControl;

internal enum NotificationSoundKind { Off, WindowsStandard, Chime }

/// <summary>One cached, embedded WAV per owner; asynchronous playback never blocks the UI.</summary>
internal sealed class NotificationSounds : IDisposable
{
    private Stream? stream;
    private SoundPlayer? player;
    private bool disposed;
    internal static NotificationSoundKind Normalize(int value) => Enum.IsDefined(typeof(NotificationSoundKind), value)
        ? (NotificationSoundKind)value : NotificationSoundKind.Chime;
    internal void Play(NotificationSoundKind sound)
    {
        if (disposed || sound == NotificationSoundKind.Off) return;
        try
        {
            if (sound == NotificationSoundKind.WindowsStandard) { SystemSounds.Asterisk.Play(); return; }
            if (player == null)
            {
                stream = typeof(NotificationSounds).Assembly.GetManifestResourceStream("WallpaperControl.NotificationChime.wav")
                    ?? throw new InvalidOperationException("Notification sound resource missing.");
                player = new SoundPlayer(stream); player.Load();
            }
            player.Play();
        }
        catch (Exception ex) { AppLogger.Info($"Notification sound unavailable: {ex.GetType().Name}."); }
    }
    public void Dispose() { if (disposed) return; disposed = true; player?.Stop(); player?.Dispose(); stream?.Dispose(); }
}
