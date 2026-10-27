using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace WallpaperControl;

internal sealed partial class WidgetManager
{
    private EmailWatcherService? emailService;
    private EmailWatcherWidgetForm? emailWidget;
    private readonly EmailPresentationModel emailPresentation = new();
    private readonly System.Windows.Forms.Timer emailTimer = new() { Interval = 500 };
    private Control? emailDispatcher;
    private int emailGeneration;
    private bool emailPresenting, emailDisposed, emailReconnectOnCommit;
    private EmailWatcherSettings emailCommitted = new();
    private Func<IEmailMailbox>? emailMailboxFactory;
    internal Action<EmailWatcherSound>? EmailSoundPlayer;
    internal Func<bool?> EmailFullscreenState=FullscreenActivityDetector.GetFullscreenState;
    internal string EmailStatus { get; private set; } = "EmailDisabled";
    private void ApplyEmailWatcher(WidgetSettings target, bool restoreLocations)
    {
        emailWidget?.Apply(target.EmailWatcher, target.ClockLanguageCode);
        if (restoreLocations && emailWidget != null) emailWidget.Location = WidgetSettings.EnsureVisible(target.EmailWatcher.Location, emailWidget.Size);
        if (previewMode) return;
        var previous=emailCommitted;
        emailCommitted = target.EmailWatcher.Clone();
        bool accountChanged=EmailWatcherStateStore.Fingerprint(previous.Account)!=EmailWatcherStateStore.Fingerprint(emailCommitted.Account);
        if(previous.DisplayMode!=emailCommitted.DisplayMode||previous.AutoHideSeconds!=emailCommitted.AutoHideSeconds)emailPresentation.Reconfigure(emailCommitted);
        if(emailService==null&&!emailCommitted.Enabled)return;
        if (emailService == null) {
            emailDispatcher = new Control(); _ = emailDispatcher.Handle;
            string directory = registryPath == WidgetSettings.RegistryPath ? EmailWatcherStateStore.DefaultDirectory
                : Path.Combine(AppContext.BaseDirectory, "EmailWatcherTests", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(registryPath))));
            emailService = new(new EmailWatcherStateStore(directory),emailMailboxFactory);
            emailService.Ready += (session, notices) => DispatchEmail(() => { if (emailService?.Generation != session) return; emailPresentation.Enqueue(notices); });
            emailService.Status += (session, status) => DispatchEmail(() => { if (emailService?.Generation == session) EmailStatus = status; });
            emailTimer.Tick += async (_, _) => await PresentEmailsAsync(); emailTimer.Start();
        }
        emailService.SetPowerSuspended(packagePowerSuspended);
        emailService.Configure(emailCommitted,emailReconnectOnCommit); emailReconnectOnCommit=false;
        emailTimer.Enabled=emailCommitted.Enabled;
        if (emailGeneration != emailService.Generation) { emailGeneration = emailService.Generation; if(accountChanged||!emailCommitted.Enabled) { emailPresentation.Reset(); emailWidget?.Hide(); emailWidget?.SetNotices([]); } else emailPresentation.DiscardWaiting(); }
        if (!emailCommitted.Enabled) emailWidget?.Hide();
    }
    private async void DispatchEmail(Action work)
    {
        try { if (!emailDisposed && emailDispatcher != null) await emailDispatcher.InvokeAsync(() => { if (!emailDisposed) work(); }); }
        catch (Exception) when (emailDisposed || emailDispatcher?.IsDisposed == true) { }
    }
    private async Task PresentEmailsAsync()
    {
        if (emailDisposed || emailPresenting || emailService == null || !emailCommitted.Enabled) return;
        emailPresenting = true;
        try {
            int session = emailService.Generation;
            bool blocked = packagePowerSuspended || emailCommitted.HoldDuringFullscreen && EmailFullscreenState() != false;
            emailPresentation.SetBlocked(blocked);
            if (blocked) { emailWidget?.Hide(); return; }
            var next = emailPresentation.Next;
            if (next.Length != 0) {
                var keys = next.Select(item => item.Key).ToArray();
                var account=emailCommitted.Account.Clone();
                if (!await emailService.ChangeAsync(session, keys, true)) { emailPresentation.Forget(keys); return; }
                if (emailDisposed) return;
                if(emailService.Generation!=session) { await emailService.RestorePendingAsync(account,keys); return; }
                if (packagePowerSuspended || emailCommitted.HoldDuringFullscreen && EmailFullscreenState() != false) { await emailService.ChangeAsync(session, keys, false); return; }
                EnsureEmailWidget();
                emailWidget!.SetNotices(emailPresentation.Active.Concat(next).ToArray());
                emailWidget.Show();
                if (!DesktopWidgetNative.AttachToDesktop(emailWidget, settings.EmailWatcher.Location)) { emailWidget.Hide(); await emailService.ChangeAsync(session, keys, false); return; }
                emailPresentation.Accept(next, emailCommitted);
                if (emailCommitted.SoundEnabled) (EmailSoundPlayer??notificationSounds.PlayEmail)(emailCommitted.Sound);
            }
            else if (emailPresentation.Active.Count != 0) { EnsureEmailWidget(); if (!emailWidget!.Visible) { emailWidget.Show(); DesktopWidgetNative.KeepOnDesktop(emailWidget); } }
            if (emailPresentation.Expired) await DismissEmailsAsync();
        }
        catch (Exception ex) { AppLogger.Info("Email presentation failed: " + ex.GetType().Name); }
        finally { emailPresenting = false; }
    }
    private void EnsureEmailWidget()
    {
        if (emailWidget != null && !emailWidget.IsDisposed) return;
        emailWidget = new(settings.EmailWatcher, settings.ClockLanguageCode,
            point => { settings.EmailWatcher.Location = point; if (!previewMode) settings.Save(registryPath); },
            async () => await DismissEmailsAsync(), OpenEmailInbox);
        RegisterDesktopWidget(emailWidget);
        emailWidget.Location = WidgetSettings.EnsureVisible(settings.EmailWatcher.Location, emailWidget.Size);
    }
    private async Task DismissEmailsAsync()
    {
        if (emailService == null || emailDisposed) return;
        int session = emailService.Generation; var keys = emailPresentation.Active.Select(item => item.Key).ToArray();
        if (await emailService.ChangeAsync(session, keys, null) && !emailDisposed && session == emailService.Generation) { emailPresentation.DismissKeys(keys); if(emailPresentation.Active.Count==0) { emailWidget?.Hide(); emailWidget?.SetNotices([]); } else emailWidget?.SetNotices(emailPresentation.Active.ToArray()); }
    }
    private async void OpenEmailInbox()
    {
        var account = emailCommitted.Account.Clone();
        try {
            if (!string.IsNullOrWhiteSpace(account.MailProgramPath)) {
                await EmailInboxLauncher.OpenProgramAsync(account.MailProgramPath);
            }
            else if (EmailAccountSettings.IsWebmailUrl(account.WebmailUrl)) {
                using var process = Process.Start(new ProcessStartInfo(new Uri(account.WebmailUrl).AbsoluteUri) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { AppLogger.Info("Email watcher: inbox launch failed; type=" + ex.GetType().Name); }
    }
    private void DisposeEmailWatcher()
    { emailDisposed = true; emailTimer.Dispose(); emailService?.Dispose(); emailWidget?.Close(); emailWidget?.Dispose(); emailDispatcher?.Dispose(); emailPresentation.Reset(); }
}
