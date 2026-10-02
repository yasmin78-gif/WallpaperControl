using Microsoft.Win32;
using System.Text.Json;

namespace WallpaperControl;

/// <summary>Only DPAPI CurrentUser ciphertext is persisted. No plaintext backup or token logging.</summary>
internal sealed class TwitchTokenStore(string registryPath = @"Software\WallpaperControl\Twitch") : ITwitchTokenStore
{
    public TwitchTokens? Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            if (key?.GetValue("Tokens") is not string encrypted) return null;
            var value = JsonSerializer.Deserialize<TwitchTokens>(WindowsSecretProtector.Unprotect(encrypted));
            if (value == null || string.IsNullOrWhiteSpace(value.AccessToken)) throw new TwitchException(TwitchError.Storage);
            return value;
        }
        catch { throw new TwitchException(TwitchError.Storage); }
    }
    public void Save(TwitchTokens tokens)
    {
        try
        {
            string encrypted = WindowsSecretProtector.Protect(JsonSerializer.Serialize(tokens));
            if (string.IsNullOrEmpty(encrypted) || JsonSerializer.Deserialize<TwitchTokens>(WindowsSecretProtector.Unprotect(encrypted))?.AccessToken != tokens.AccessToken)
                throw new TwitchException(TwitchError.Storage);
            using var key = Registry.CurrentUser.CreateSubKey(registryPath);
            key.SetValue("Tokens", encrypted, RegistryValueKind.String);
        }
        catch { throw new TwitchException(TwitchError.Storage); }
    }
    public void Clear()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(registryPath, true); key?.DeleteValue("Tokens", false); }
        catch { throw new TwitchException(TwitchError.Storage); }
    }
}
