using System.Text.Json;

namespace WallpaperControl;

internal sealed record EmailPending(uint Uid, bool Presented = false);
internal sealed class EmailSyncState
{
    public int Version { get; set; } = 1;
    public string Fingerprint { get; set; } = "";
    public bool Initialized { get; set; }
    public uint UidValidity { get; set; }
    public uint Through { get; set; }
    public List<EmailPending> Pending { get; set; } = [];
    internal bool Baseline(uint validity, uint highest)
    {
        if (Initialized && UidValidity == validity) return false;
        Initialized = true; UidValidity = validity; Through = highest; Pending.Clear(); return true;
    }
    internal void Process(uint uid, bool matches)
    {
        if (uid <= Through) return;
        if (matches && !Pending.Any(item => item.Uid == uid)) {
            if (Pending.Count >= 10000) throw new IOException("Email pending capacity reached.");
            Pending.Add(new(uid));
        }
        Through = uid;
    }
}

/// <summary>Only identifiers and progress, encrypted for the Windows user. Never message content.</summary>
internal sealed class EmailWatcherStateStore(string directory)
{
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperControl", "EmailWatcher");
    private string PathFor(Guid id) => Path.Combine(directory, id.ToString("N") + ".state");
    private void CheckPath()
    {
        for (var item = new DirectoryInfo(directory); item != null; item = item.Parent)
            if (item.Exists && item.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Redirected email state directory.");
    }
    internal static string Fingerprint(EmailAccountSettings account) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(account.Id + "\n" + account.Server.ToLowerInvariant() + "\n" + account.Port + "\n" + account.Security + "\n" + account.Username)));
    internal EmailSyncState Load(EmailAccountSettings account)
    {
        string fingerprint = Fingerprint(account);
        try {
            CheckPath(); string path = PathFor(account.Id);
            if (!File.Exists(path)) return new() { Fingerprint = fingerprint };
            if (new FileInfo(path).Length > 2_000_000 || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException();
            string plain = WindowsSecretProtector.Unprotect(File.ReadAllText(path));
            var state = JsonSerializer.Deserialize<EmailSyncState>(plain);
            if (state == null || state.Version != 1 || state.Fingerprint != fingerprint || state.Pending == null || state.Pending.Count > 10000
                || state.Pending.Any(item => item == null || item.Uid == 0 || item.Uid > state.Through)
                || state.Initialized && state.UidValidity == 0) throw new InvalidDataException();
            state.Pending = state.Pending.DistinctBy(item => item.Uid).ToList(); return state;
        }
        catch (Exception) { AppLogger.Info("Email watcher: synchronization baseline reset."); return new() { Fingerprint = fingerprint }; }
    }
    internal void Save(Guid account, EmailSyncState state)
    {
        CheckPath(); Directory.CreateDirectory(directory); CheckPath();
        string path = PathFor(account), temporary = path + ".tmp";
        foreach (string file in new[] { path, temporary })
            if (File.Exists(file) && File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Redirected email state file.");
        string encrypted = WindowsSecretProtector.Protect(JsonSerializer.Serialize(state));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(encrypted); stream.Write(bytes); stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }
}
