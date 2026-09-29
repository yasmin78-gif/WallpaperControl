using Microsoft.Win32;
using System.Diagnostics;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperControl;

/// <summary>Explicit secret access only; ordinary formatting, debugging and JSON never reveal the key.</summary>
[DebuggerDisplay("TrackingCredential (redacted)")]
[JsonConverter(typeof(TrackingCredentialJsonConverter))]
internal sealed class TrackingCredential
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)] private readonly string secret;
    internal TrackingCredential(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 16384)
            throw new ArgumentException("Invalid tracking credential.");
        this.secret = secret;
    }
    /// <summary>For a future provider adapter only. Never log or serialize the returned string.</summary>
    internal string Reveal() => secret;
    public override string ToString() => "TrackingCredential (redacted)";
}

internal sealed class TrackingCredentialJsonConverter : JsonConverter<TrackingCredential>
{
    public override TrackingCredential Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        throw new NotSupportedException("Credentials cannot be deserialized as ordinary application data.");
    public override void Write(Utf8JsonWriter writer, TrackingCredential value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Credentials cannot be serialized as ordinary application data.");
}

internal enum TrackingCredentialLoadResult { Missing, Loaded, Recovered, Unreadable }

/// <summary>HKCU ciphertext only, using the existing CurrentUser WindowsSecretProtector.
/// Registry values are replaced individually; a previous valid ciphertext is retained for recovery.</summary>
internal sealed class TrackingCredentialStore
{
    internal const string DefaultRegistryPath = @"Software\WallpaperControl\TrackingProviders";
    private readonly string registryPath;
    internal TrackingCredentialStore(string registryPath = DefaultRegistryPath) => this.registryPath = registryPath;
    private static bool ValidProvider(string provider) => !string.IsNullOrEmpty(provider) && provider.Length <= 80
        && provider.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private static bool Decode(string? encrypted, out TrackingCredential? credential)
    {
        credential = null;
        if (string.IsNullOrEmpty(encrypted) || encrypted.Length > 100000) return false;
        try
        {
            string plain = WindowsSecretProtector.Unprotect(encrypted);
            if (plain.Length == 0) return false;
            using var json = JsonDocument.Parse(plain);
            if (json.RootElement.GetProperty("Version").GetInt32() != 1) return false;
            credential = new TrackingCredential(json.RootElement.GetProperty("Secret").GetString()!);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        { return false; }
    }

    internal TrackingCredentialLoadResult Load(string provider, out TrackingCredential? credential)
    {
        credential = null;
        if (!ValidProvider(provider)) return TrackingCredentialLoadResult.Unreadable;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryPath);
            object? current = key?.GetValue(provider);
            object? backup = key?.GetValue(provider + ".bak");
            if (current == null && backup == null) return TrackingCredentialLoadResult.Missing;
            if (Decode(current as string, out credential)) return TrackingCredentialLoadResult.Loaded;
            if (Decode(backup as string, out credential))
            { AppLogger.Info("Tracking credential recovered from protected backup."); return TrackingCredentialLoadResult.Recovered; }
            Log(); return TrackingCredentialLoadResult.Unreadable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        { Log(); return TrackingCredentialLoadResult.Unreadable; }
    }

    internal bool Save(string provider, TrackingCredential credential)
    {
        if (!ValidProvider(provider) || credential == null) return false;
        try
        {
            string json = JsonSerializer.Serialize(new { Version = 1, Secret = credential.Reveal() });
            string encrypted = WindowsSecretProtector.Protect(json);
            if (!Decode(encrypted, out var verified) || verified!.Reveal() != credential.Reveal()) { Log(); return false; }
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(registryPath, true);
            object? previous = key.GetValue(provider);
            if (previous is string prior && Decode(prior, out _)) key.SetValue(provider + ".bak", prior, RegistryValueKind.String);
            try
            {
                key.SetValue(provider, encrypted, RegistryValueKind.String);
                if (!Equals(key.GetValue(provider), encrypted)) throw new IOException("Credential write verification failed.");
                return true;
            }
            catch
            {
                if (previous is string old) key.SetValue(provider, old, RegistryValueKind.String);
                else if (previous == null) key.DeleteValue(provider, false);
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException or ArgumentException)
        { Log(); return false; }
    }

    internal bool Delete(string provider)
    {
        if (!ValidProvider(provider)) return false;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryPath, true);
            // Remove backup first, so a deleted key cannot reappear via recovery.
            key?.DeleteValue(provider + ".bak", false);
            key?.DeleteValue(provider, false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        { Log(); return false; }
    }
    private static void Log() => AppLogger.Warning("Tracking credential storage operation failed.",
        new InvalidOperationException("Credential details omitted."));
}
