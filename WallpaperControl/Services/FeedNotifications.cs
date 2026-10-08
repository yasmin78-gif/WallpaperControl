using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace WallpaperControl;
internal sealed class FeedNotifications
{
    private readonly string? path;
    private readonly Dictionary<string,List<string>> history;
    private readonly HashSet<string> baseline = [];
    private HashSet<string> enabled = [];
    internal event Action<FeedSource,int>? NewEntries;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Key(FeedSource source) => Hash(source.Id + ":" + source.Url);
    internal FeedNotifications(string? path)
    {
        this.path = path; history = [];
        try
        {
            if (path != null && File.Exists(path) && new FileInfo(path).Length <= 4_000_000)
                foreach (var pair in JsonSerializer.Deserialize<Dictionary<string,List<string>>>(File.ReadAllText(path)) ?? [])
                    history[pair.Key] = (pair.Value ?? []).TakeLast(2048).ToList();
        }
        catch (Exception ex) { AppLogger.Info($"Feed notification history unavailable: {ex.GetType().Name}"); }
    }
    internal void Rebaseline() => baseline.Clear();
    internal void Configure(IEnumerable<FeedSource> sources)
    {
        var values = sources.ToArray(); var next = values.Where(s=>s.Notifications).Select(Key).ToHashSet();
        foreach (var key in next.Except(enabled)) baseline.Remove(key);
        enabled = next;
        var valid = values.Select(Key).ToHashSet(); foreach (var key in history.Keys.Where(k=>!valid.Contains(k)).ToArray()) { history.Remove(key); baseline.Remove(key); }
    }
    internal void Observe(FeedSource source, ParsedFeed feed, bool suppressed)
    {
        string key = Key(source); bool initial = baseline.Add(key);
        if (!history.TryGetValue(key,out var seen)) history[key] = seen = [];
        var identities = feed.Entries.Select(e => Hash(e.Id)).Distinct().ToArray();
        var fresh = identities.Where(id=>!seen.Contains(id)).ToArray();
        if (fresh.Length == 0) return;
        seen.AddRange(fresh); if (seen.Count > 2048) seen.RemoveRange(0,seen.Count-2048);
        bool saved = Save();
        if (saved && source.Notifications && !initial && !suppressed)
        { try { NewEntries?.Invoke(source,fresh.Length); } catch (Exception ex) { AppLogger.Info($"Feed notification unavailable: {ex.GetType().Name}"); } }
    }
    private bool Save()
    {
        if (path == null) return true;
        string temporary = path + ".tmp";
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(temporary,JsonSerializer.Serialize(history)); File.Move(temporary,path,true); return true; }
        catch (Exception ex) { AppLogger.Info($"Feed notification history save failed: {ex.GetType().Name}"); return false; }
    }
}
