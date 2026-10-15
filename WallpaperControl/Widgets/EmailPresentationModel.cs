namespace WallpaperControl;

/// <summary>UI-only presentation state. Durable pending identities remain in the service.</summary>
internal sealed class EmailPresentationModel(Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<EmailMessageKey, EmailNotice> waiting = [];
    internal List<EmailNotice> Active { get; } = [];
    internal bool Blocked { get; private set; }
    internal DateTimeOffset? Deadline { get; private set; }
    private DateTimeOffset? blockedAt;
    internal void Enqueue(IEnumerable<EmailNotice> notices)
    { foreach (var notice in notices) if (!Active.Any(item => item.Key == notice.Key)) waiting[notice.Key] = notice; }
    internal void DiscardWaiting()=>waiting.Clear();
    internal void Reconfigure(EmailWatcherSettings settings) { if(Active.Count==0)return; Deadline=settings.DisplayMode==EmailWatcherDisplayMode.Automatic?now().AddSeconds(settings.AutoHideSeconds):null; if(Blocked)blockedAt=now(); }
    internal void Forget(IEnumerable<EmailMessageKey> keys) { foreach(var key in keys)waiting.Remove(key); }
    internal EmailNotice[] Next => Blocked ? [] : waiting.Values.OrderBy(item => item.Received).Take(Math.Max(0, 100 - Active.Count)).ToArray();
    internal void Accept(IEnumerable<EmailNotice> notices, EmailWatcherSettings settings)
    {
        bool added=false;
        foreach (var notice in notices) { waiting.Remove(notice.Key); if (!Active.Any(item => item.Key == notice.Key)) { Active.Add(notice); added=true; } }
        if(added)Deadline = settings.DisplayMode == EmailWatcherDisplayMode.Automatic ? now().AddSeconds(settings.AutoHideSeconds) : null;
    }
    internal void SetBlocked(bool value)
    {
        if (Blocked == value) return;
        Blocked = value;
        if (value) blockedAt = now();
        else { if (Deadline.HasValue && blockedAt.HasValue) Deadline += now() - blockedAt.Value; blockedAt = null; }
    }
    internal bool Expired => !Blocked && Deadline.HasValue && now() >= Deadline;
    internal void DismissKeys(IEnumerable<EmailMessageKey> keys) { var ids=keys.ToHashSet(); Active.RemoveAll(item=>ids.Contains(item.Key)); if(Active.Count==0)Deadline=null; }
    internal EmailMessageKey[] Dismiss() { var keys = Active.Select(item => item.Key).ToArray(); Active.Clear(); Deadline = null; return keys; }
    internal void Reset() { waiting.Clear(); Active.Clear(); Deadline = null; blockedAt = null; Blocked = false; }
}
