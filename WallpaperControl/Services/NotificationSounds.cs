using System.Media;

namespace WallpaperControl;

internal enum NotificationSoundKind { Off, WindowsStandard, Chime, Soft, Bell, Triad }

/// <summary>One cached, embedded WAV per owner; asynchronous playback never blocks the UI.</summary>
internal sealed class NotificationSounds : IDisposable
{
    private Stream? stream;
    private SoundPlayer? player;
    private bool disposed;
    private NotificationSoundKind loadedSound;
    internal static NotificationSoundKind Normalize(int value) => Enum.IsDefined(typeof(NotificationSoundKind), value)
        ? (NotificationSoundKind)value : NotificationSoundKind.Chime;
    internal void Play(NotificationSoundKind sound)
    {
        if (disposed || sound == NotificationSoundKind.Off) return;
        try
        {
            if (sound == NotificationSoundKind.WindowsStandard) { SystemSounds.Asterisk.Play(); return; }
            if (player == null || loadedSound != sound)
            {
                player?.Stop(); player?.Dispose(); stream?.Dispose(); player = null; stream = null;
                string asset = sound switch { NotificationSoundKind.Soft => "Soft", NotificationSoundKind.Bell => "Bell", NotificationSoundKind.Triad => "Triad", _ => "Chime" };
                stream = typeof(NotificationSounds).Assembly.GetManifestResourceStream($"WallpaperControl.Notification{asset}.wav")
                    ?? throw new InvalidOperationException("Notification sound resource missing.");
                player = new SoundPlayer(stream); player.Load(); loadedSound = sound;
            }
            player.Play();
        }
        catch (Exception ex) { AppLogger.Info($"Notification sound unavailable: {ex.GetType().Name}."); }
    }
    public void Dispose() { if (disposed) return; disposed = true; player?.Stop(); player?.Dispose(); stream?.Dispose(); }
}
