using System.Text.Json;
namespace WallpaperControl.Remote.AndroidApp;

// Shared by the foreground session and cold-start native widgets. Same existing key and Keystore.
internal static class RemoteCredentialStore
{
    private const string StorageKey = "wallpapercontrol.remote.connection.v1";
    public static async Task<SavedRemote?> ReadAsync()
    {
        string? json = await SecureStorage.Default.GetAsync(StorageKey);
        return json == null ? null : JsonSerializer.Deserialize(json, RemoteJson.Type<SavedRemote>());
    }
    public static Task WriteAsync(SavedRemote saved) => SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(saved, RemoteJson.Type<SavedRemote>()));
    public static void Remove() => SecureStorage.Default.Remove(StorageKey);
}
