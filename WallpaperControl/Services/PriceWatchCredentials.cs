namespace WallpaperControl;

internal sealed class PriceWatchCredentials(string registryPath = @"Software\WallpaperControl\PriceWatchCredentials")
{
    private const string Key = "pricewatch";
    internal static PriceWatchCredentials Current { get; } = new();
    private readonly TrackingCredentialStore store = new(registryPath);

    internal TrackingCredential? Load()
    {
        store.Load(Key, out var credentials);
        return credentials;
    }

    internal bool Save(string user, string password)
    {
        if (user.Length is < 1 or > 128 || user.Any(c => c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z'
            or >= '0' and <= '9' or '_' or '-' or '.' or '@'))
            || password.Length is < 1 or > 1024 || password.Any(char.IsControl))
            return false;
        return store.Save(Key, new TrackingCredential(user + ":" + password));
    }

    internal bool Delete() => store.Delete(Key);
}
