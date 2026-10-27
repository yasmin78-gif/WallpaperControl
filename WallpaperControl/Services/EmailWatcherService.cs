using System.Text.Json;

namespace WallpaperControl;

/// <summary>One serialized connection; background IO continues while presentation is hidden.</summary>
internal sealed class EmailWatcherService : IDisposable
{
    private readonly EmailWatcherStateStore store;
    private readonly Func<IEmailMailbox> createMailbox;
    private readonly SemaphoreSlim stateGate = new(1);
    private CancellationTokenSource? cancellation;
    private Task worker = Task.CompletedTask;
    private EmailSyncState? state;
    private EmailWatcherSettings? configuration;
    private string identity = "";
    private volatile int generation;
    private volatile bool disposed;
    private volatile bool powerSuspended;
    internal event Action<int, IReadOnlyList<EmailNotice>>? Ready;
    internal event Action<int, string>? Status;
    internal int Generation => generation;
    internal Task Completion => worker;
    internal EmailWatcherService(EmailWatcherStateStore store, Func<IEmailMailbox>? factory = null)
    { this.store = store; createMailbox = factory ?? (() => new EmailMailbox()); }
    internal void Configure(EmailWatcherSettings value,bool retryCompleted=false, bool forceRestart=false)
    {
        if (disposed) return;
        string next = JsonSerializer.Serialize(new { value.Enabled, Account=new { value.Account.Id,value.Account.Server,value.Account.Port,value.Account.Security,value.Account.Authentication,value.Account.Username,value.Account.ProtectedPassword,value.Account.PollingSeconds }, value.Rules, value.ShowPreview, value.PreviewCharacters });
        if (!forceRestart && identity == next && (!retryCompleted||!worker.IsCompleted)) return;
        identity = next; int session = ++generation; var previous = worker; var old = cancellation;
        old?.Cancel(); cancellation = new(); var token = cancellation.Token; var options = value.Clone(); configuration = options;
        worker = Task.Run(async () => {
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            old?.Dispose();
            if (token.IsCancellationRequested || disposed || powerSuspended) return;
            await stateGate.WaitAsync().ConfigureAwait(false);
            try { state = null; } finally { stateGate.Release(); }
            if (!options.Enabled) { Status?.Invoke(session, "EmailDisabled"); return; }
            if (!options.Account.IsConfigured) { Status?.Invoke(session, "EmailInvalidConfiguration"); return; }
            await MonitorAsync(options, session, token).ConfigureAwait(false);
        });
    }
    internal void SetPowerSuspended(bool value)
    {
        if (disposed || powerSuspended == value) return;
        powerSuspended = value;
        if (value) cancellation?.Cancel();
        else if (configuration != null) {
            AppLogger.Info("Email watcher: resuming; reconnecting from saved checkpoint.");
            Configure(configuration, forceRestart: true);
        }
    }
    private async Task MonitorAsync(EmailWatcherSettings options, int session, CancellationToken token)
    {
        int failures = 0;
        int attempt = 0;
        while (!token.IsCancellationRequested)
        {
            string phase = "connect/authenticate/open-inbox";
            attempt++;
            try {
                Status?.Invoke(session, "EmailConnecting");
                await using var mailbox = createMailbox();
                await mailbox.ConnectAsync(options.Account, token).ConfigureAwait(false);
                phase = "initialize";
                if(mailbox.Validity==0)throw new InvalidDataException();
                uint baselineHighest=await mailbox.HighestAsync(token).ConfigureAwait(false);
                await stateGate.WaitAsync(token).ConfigureAwait(false);
                try {
                    bool freshSession=state==null;
                    state = store.Load(options.Account);
                    bool baseline=state.Baseline(mailbox.Validity, baselineHighest);
                    AppLogger.Info($"Email watcher: synchronization initialized; freshBaseline={baseline}; checkpoint={state.Through}; highest={baselineHighest}; pending={state.Pending.Count}.");
                    bool retired=freshSession&&state.Pending.RemoveAll(item=>item.Presented)>0;
                    if(baseline||retired)store.Save(options.Account.Id,state);
                } finally { stateGate.Release(); }
                phase = "monitor";
                Status?.Invoke(session, "EmailConnected");
                var cache = new Dictionary<uint, EmailNotice>();
                while (!token.IsCancellationRequested)
                {
                    uint through;
                    await stateGate.WaitAsync(token).ConfigureAwait(false);
                    try { through = state!.Through; } finally { stateGate.Release(); }
                    int batches=0; bool catchUp=false;
                    while (!token.IsCancellationRequested)
                    {
                        if(batches++>=4) { catchUp=true; break; }
                        await stateGate.WaitAsync(token).ConfigureAwait(false);
                        bool full;
                        try { full=state!.Pending.Count>9744; } finally { stateGate.Release(); }
                        if(full) { Status?.Invoke(session,"EmailQueueFull"); break; }
                        var uids=await mailbox.NewUidsAsync(through,token).ConfigureAwait(false);
                        if(uids.Count==0)break;
                        uint last=uids.Max();
                        var candidates = await mailbox.FetchAsync(through + 1, last, token).ConfigureAwait(false);
                        await stateGate.WaitAsync(token).ConfigureAwait(false);
                        try {
                            foreach (var candidate in candidates.OrderBy(item => item.Uid))
                                state!.Process(candidate.Uid, EmailRuleMatcher.Matches(options.Rules, candidate.Addresses, candidate.Subject));
                            state!.Through = last;
                            store.Save(options.Account.Id, state);
                        } finally { stateGate.Release(); }
                        through = last;
                    }
                    uint[] pending;
                    await stateGate.WaitAsync(token).ConfigureAwait(false);
                    try { pending = state!.Pending.Where(item => !item.Presented).Select(item => item.Uid).Take(50).ToArray(); }
                    finally { stateGate.Release(); }
                    foreach (var uid in cache.Keys.Except(pending).ToArray()) cache.Remove(uid);
                    foreach (uint uid in pending)
                    {
                        if (cache.ContainsKey(uid)) continue;
                        var candidate = await mailbox.FindAsync(uid, token).ConfigureAwait(false);
                        if (candidate == null) { await ChangeAsync(session, [EmailMessageKey.Inbox(options.Account.Id, mailbox.Validity, uid)], null).ConfigureAwait(false); continue; }
                        string preview = "";
                        if (options.ShowPreview) {
                            try { preview = await mailbox.PreviewAsync(uid, token).ConfigureAwait(false); }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception) { AppLogger.Info("Email watcher: preview unavailable."); }
                        }
                        cache[uid] = new(EmailMessageKey.Inbox(options.Account.Id, mailbox.Validity, uid), candidate.SenderName,
                            EmailText.Limit(string.Join(", ", candidate.Addresses), 500), EmailText.Limit(candidate.Subject, 2000), candidate.Received, EmailText.Limit(preview, options.PreviewCharacters));
                    }
                    if (session == generation && !token.IsCancellationRequested) Ready?.Invoke(session, cache.Values.OrderByDescending(item => item.Received).ToArray());
                    if(catchUp)continue; // Publish bounded batches before scanning a large backlog further.
                    await mailbox.WaitAsync(options.Account.PollingSeconds, token).ConfigureAwait(false); failures=0;
                    if (mailbox.Validity != state!.UidValidity) break; // Reopen and establish a safe fresh baseline.
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (MailKit.Security.AuthenticationException ex) { EmailConnectionDiagnostics.LogFailure(ex, phase, attempt, false); Status?.Invoke(session, "EmailAuthenticationFailed"); return; }
            catch (MailKit.Security.SslHandshakeException ex) { EmailConnectionDiagnostics.LogFailure(ex, phase, attempt, false); Status?.Invoke(session, "EmailCertificateFailed"); return; }
            catch (Exception ex) {
                // Exception messages and protocol responses can contain credentials or message data.
                EmailConnectionDiagnostics.LogFailure(ex, phase, attempt, true);
                Status?.Invoke(session, ex is IOException ? "EmailStorageOrNetworkFailed" : "EmailConnectionFailed");
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(++failures, 6))) + Random.Shared.NextDouble()), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
    internal Task<bool> ChangeAsync(int session, IReadOnlyList<EmailMessageKey> keys, bool? presented) => Task.Run(async () => {
        await stateGate.WaitAsync().ConfigureAwait(false);
        try {
            if (disposed || generation != session || state == null || configuration == null
                || keys.Any(key => key.AccountId != configuration.Account.Id || presented!=null && key.UidValidity != state.UidValidity || key.Folder != "INBOX")) return false;
            if(presented==true && keys.Any(key=>!state.Pending.Any(item=>item.Uid==key.Uid&&!item.Presented)))return false;
            var ids = keys.Where(key=>key.UidValidity==state.UidValidity).Select(key => key.Uid).ToHashSet();
            var previous = state.Pending.ToList();
            if (presented == null) state.Pending.RemoveAll(item => ids.Contains(item.Uid));
            else state.Pending = state.Pending.Select(item => ids.Contains(item.Uid) ? item with { Presented = presented.Value } : item).ToList();
            try { store.Save(configuration.Account.Id, state); }
            catch (Exception) { state.Pending = previous; Status?.Invoke(session, "EmailStorageOrNetworkFailed"); return false; }
            return true;
        } finally { stateGate.Release(); }
    });
    internal Task RestorePendingAsync(EmailAccountSettings account,IReadOnlyList<EmailMessageKey> keys)=>Task.Run(async()=> {
        await stateGate.WaitAsync().ConfigureAwait(false);
        try {
            if(disposed||configuration==null||EmailWatcherStateStore.Fingerprint(configuration.Account)!=EmailWatcherStateStore.Fingerprint(account))return;
            var current=state??store.Load(account);
            if(!current.Initialized||keys.Any(key=>key.AccountId!=account.Id||key.UidValidity!=current.UidValidity||key.Uid==0||key.Uid>current.Through))return;
            var ids=keys.Select(key=>key.Uid).ToHashSet(); var previous=current.Pending.ToList();
            current.Pending=current.Pending.Select(item=>ids.Contains(item.Uid)?item with { Presented=false }:item).ToList();
            foreach(uint uid in ids)if(!current.Pending.Any(item=>item.Uid==uid))current.Pending.Add(new(uid));
            try { store.Save(account.Id,current); }
            catch(Exception) { current.Pending=previous; Status?.Invoke(generation,"EmailStorageOrNetworkFailed"); }
        } finally { stateGate.Release(); }
    });
    public void Dispose() { if (disposed) return; disposed = true; generation++; cancellation?.Cancel(); _ = worker.ContinueWith(_ => { cancellation?.Dispose();  }, TaskScheduler.Default); }
}
