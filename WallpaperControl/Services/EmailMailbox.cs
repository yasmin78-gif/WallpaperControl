using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace WallpaperControl;

internal sealed record EmailCandidate(uint Uid, string SenderName, string[] Addresses, string Subject, DateTimeOffset Received)
{ public override string ToString()=>"EmailCandidate (redacted)"; }
internal sealed record EmailNotice(EmailMessageKey Key, string SenderName, string Address, string Subject, DateTimeOffset Received, string Preview)
{ public override string ToString()=>"EmailNotice (redacted)"; }
internal interface IEmailMailbox : IAsyncDisposable
{
    uint Validity { get; }
    uint NextUid { get; }
    Task ConnectAsync(EmailAccountSettings account, CancellationToken cancellation);
    Task<uint> HighestAsync(CancellationToken cancellation);
    Task<IReadOnlyList<uint>> NewUidsAsync(uint after, CancellationToken cancellation);
    Task<IReadOnlyList<EmailCandidate>> FetchAsync(uint first, uint last, CancellationToken cancellation);
    Task<EmailCandidate?> FindAsync(uint uid, CancellationToken cancellation);
    Task<string> PreviewAsync(uint uid, CancellationToken cancellation);
    Task WaitAsync(int pollingSeconds, CancellationToken cancellation);
}

internal sealed class EmailMailbox : IEmailMailbox
{
    static EmailMailbox()=>System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    private readonly ImapClient client;
    private IMailFolder? monitored;
    private int changes;
    private void LatchChange(object? sender, EventArgs args) => Interlocked.Exchange(ref changes, 1);
    internal EmailMailbox(ImapClient? transport = null) => client = transport ?? new() { Timeout = 30000 };
    private CancellationTokenSource CommandDeadline(CancellationToken parent)
    { var source=CancellationTokenSource.CreateLinkedTokenSource(parent); source.CancelAfter(Math.Max(1000,client.Timeout)); return source; }
    private IMailFolder Inbox => client.Inbox;
    public uint Validity => Inbox.UidValidity;
    public uint NextUid => Inbox.UidNext?.Id ?? throw new InvalidDataException();
    public async Task ConnectAsync(EmailAccountSettings account, CancellationToken cancellation)
    {
        using var deadline=CommandDeadline(cancellation); cancellation=deadline.Token;
        if (!account.IsConfigured) throw new ArgumentException("Invalid email configuration.");
        string password = WindowsSecretProtector.Unprotect(account.ProtectedPassword);
        if (password.Length == 0) throw new MailKit.Security.AuthenticationException();
        await client.ConnectAsync(account.Server, account.Port, account.Security == EmailTransportSecurity.Tls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellation).ConfigureAwait(false);
        await client.AuthenticateAsync(account.Username, password, cancellation).ConfigureAwait(false);
        await Inbox.OpenAsync(FolderAccess.ReadOnly, cancellation).ConfigureAwait(false);
        monitored=Inbox; monitored.CountChanged+=LatchChange;
    }
    private static EmailCandidate Candidate(IMessageSummary item) => new(item.UniqueId.Id,
        EmailText.Limit(item.Envelope?.From.Mailboxes.FirstOrDefault()?.Name ?? "", 150),
        item.Envelope?.From.Mailboxes.Select(address => address.Address).Take(32).ToArray() ?? [],
        item.Envelope?.Subject ?? "", item.InternalDate ?? DateTimeOffset.UtcNow);
    public async Task<uint> HighestAsync(CancellationToken cancellation)
    {
        using var deadline=CommandDeadline(cancellation); cancellation=deadline.Token;
        if (Inbox.Count == 0) return 0;
        var items = await Inbox.FetchAsync(Inbox.Count - 1, -1, MessageSummaryItems.UniqueId, cancellation).ConfigureAwait(false);
        return items.Count == 0 ? 0 : items.Max(item => item.UniqueId.Id);
    }
    public async Task<IReadOnlyList<uint>> NewUidsAsync(uint after, CancellationToken cancellation)
    {
        using var deadline=CommandDeadline(cancellation); cancellation=deadline.Token;
        if (after == uint.MaxValue) return [];
        // UIDNEXT need not refresh with EXISTS. Strict filtering also excludes old UIDs from a reversed '*' range.
        var values = await Inbox.SearchAsync(SearchQuery.Uids(new UniqueIdRange(new UniqueId(after + 1), UniqueId.MaxValue)), cancellation).ConfigureAwait(false);
        return values.Where(uid => uid.Id > after).OrderBy(uid => uid.Id).Take(256).Select(uid => uid.Id).ToArray();
    }
    public async Task<IReadOnlyList<EmailCandidate>> FetchAsync(uint first, uint last, CancellationToken cancellation)
    {
        using var deadline=CommandDeadline(cancellation); cancellation=deadline.Token;
        if (first == 0 || last < first) return [];
        var summaries = await Inbox.FetchAsync(new UniqueIdRange(new UniqueId(first), new UniqueId(last)), MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate, cancellation).ConfigureAwait(false);
        return summaries.Where(item => item.UniqueId.Id >= first && item.UniqueId.Id <= last).OrderBy(item => item.UniqueId.Id).Select(Candidate).ToArray();
    }
    public async Task<EmailCandidate?> FindAsync(uint uid, CancellationToken cancellation) => (await FetchAsync(uid, uid, cancellation).ConfigureAwait(false)).FirstOrDefault();
    public async Task<string> PreviewAsync(uint uid, CancellationToken cancellation)
    {
        using var deadline=CommandDeadline(cancellation); cancellation=deadline.Token;
        var items = await Inbox.FetchAsync(new[] { new UniqueId(uid) }, MessageSummaryItems.BodyStructure | MessageSummaryItems.UniqueId, cancellation).ConfigureAwait(false);
        var part = items.FirstOrDefault(item => item.UniqueId.Id == uid)?.TextBody;
        if (part == null || part.IsAttachment) return "";
        // MailKit GetStream uses BODY.PEEK; only a bounded plain text section, never the whole message.
        using var stream = await Inbox.GetStreamAsync(new UniqueId(uid), (string.IsNullOrEmpty(part.PartSpecifier) ? "TEXT" : part.PartSpecifier), 0, 65536, cancellation).ConfigureAwait(false);
        using var decoded = new MemoryStream();
        using var content = new MimeContent(stream, part.ContentTransferEncoding?.ToLowerInvariant() switch { "base64" => ContentEncoding.Base64, "quoted-printable" => ContentEncoding.QuotedPrintable, _ => ContentEncoding.Default });
        content.DecodeTo(decoded, cancellation); decoded.Position = 0;
        string charset = part.ContentType.Charset ?? "utf-8";
        System.Text.Encoding textEncoding;
        try { textEncoding = System.Text.Encoding.GetEncoding(charset); } catch (Exception ex) when(ex is ArgumentException or NotSupportedException) { textEncoding = System.Text.Encoding.UTF8; }
        return EmailText.Limit(textEncoding.GetString(decoded.ToArray()), 300);
    }
    public async Task WaitAsync(int pollingSeconds, CancellationToken cancellation)
    {
        if (!client.Capabilities.HasFlag(ImapCapabilities.Idle)) {
            await Task.Delay(TimeSpan.FromSeconds(pollingSeconds), cancellation).ConfigureAwait(false);
            using var command=CommandDeadline(cancellation);
            await client.NoOpAsync(command.Token).ConfigureAwait(false); return;
        }
        if(Interlocked.Exchange(ref changes,0)!=0)return;
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(pollingSeconds,15,60)));
        using var operation=new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(pollingSeconds,15,60)+10));
        using var registration = cancellation.Register(() => { done.Cancel(); operation.CancelAfter(TimeSpan.FromSeconds(3)); });
        void Changed(object? sender, EventArgs args) => done.Cancel();
        Inbox.CountChanged += Changed;
        try { await client.IdleAsync(done.Token, operation.Token).ConfigureAwait(false); }
        finally { Inbox.CountChanged -= Changed; Interlocked.Exchange(ref changes,0); }
    }
    public async ValueTask DisposeAsync()
    {
        if(monitored!=null)monitored.CountChanged-=LatchChange;
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); if (client.IsConnected) await client.DisconnectAsync(true, timeout.Token).ConfigureAwait(false); }
        catch (Exception) { }
        client.Dispose();
    }
    internal static async Task<string> TestAsync(EmailAccountSettings account, CancellationToken cancellation)
    {
        try { await using var mailbox = new EmailMailbox(); await mailbox.ConnectAsync(account, cancellation).ConfigureAwait(false); _ = mailbox.Validity; return "EmailConnected"; }
        catch (OperationCanceledException ex) { if (!cancellation.IsCancellationRequested) EmailConnectionDiagnostics.LogFailure(ex, "connection-test", 1, false); return cancellation.IsCancellationRequested?"EmailCancelled":"EmailConnectionFailed"; }
        catch (MailKit.Security.AuthenticationException ex) { EmailConnectionDiagnostics.LogFailure(ex, "connection-test", 1, false); return "EmailAuthenticationFailed"; }
        catch (SslHandshakeException ex) { EmailConnectionDiagnostics.LogFailure(ex, "connection-test", 1, false); return "EmailCertificateFailed"; }
        catch (Exception ex) { EmailConnectionDiagnostics.LogFailure(ex, "connection-test", 1, false); return "EmailConnectionFailed"; }
    }
}

internal static class EmailText
{
    internal static string Limit(string value, int maximum)
    {
        value=value.Replace("\r\n","\n").Replace('\r','\n');
        value = new string(value.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Replace('\n', ' ').Replace('\t', ' ');
        var indices = System.Globalization.StringInfo.ParseCombiningCharacters(value);
        return indices.Length <= maximum ? value : value[..indices[maximum]] + "\u2026";
    }
}
