extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Win32;

internal static class EmailWatcherIntegrationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Members)!.GetValue(value)!;
    private static string root = "";
    internal static void Run(Action<bool, string> check)
    {
        root = Path.Combine(AppContext.BaseDirectory, "email-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            Foundations(check); Service(check).GetAwaiter().GetResult(); EmailImapProtocolTests.Run(check).GetAwaiter().GetResult();
            Exception? failure = null; var thread = new Thread(() => { try { Ui(check); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure != null) throw failure;
        } finally {
            string allowed=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"email-tests"))+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(root).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe test cleanup path.");
            Directory.Delete(root, true);
        }
    }
    internal static void Package(string folder, Action<bool,string> check)
    {
        var executable=File.ReadAllBytes(Path.Combine(folder,"WallpaperControl.exe"));
        foreach(var tone in new[]{App.EmailWatcherSound.Magical,App.EmailWatcherSound.Premium,App.EmailWatcherSound.Fanfare}) {
            using var input=typeof(App.WidgetSettings).Assembly.GetManifestResourceStream(App.EmailWatcherSoundResources.ResourceName(tone)!)!;
            using var bytes=new MemoryStream(); input.CopyTo(bytes);
            check(executable.AsSpan().IndexOf(bytes.ToArray())>=0,"Published single-file executable contains unchanged WAV: "+tone);
        }
        check(executable.AsSpan().IndexOf(Encoding.UTF8.GetBytes("MailKit"))>=0,"MailKit included in self-contained publish");
    }
    private static App.EmailWatcherSettings Settings() => new() {
        Enabled = true, Account = new() { DisplayName = "Synthetic account", Server = "imap.example.invalid", Username = "sample@example.invalid", WebmailUrl = "https://webmail.example.invalid/", ProtectedPassword = App.WindowsSecretProtector.Protect("synthetic-app-secret") },
        Rules = [new() { SenderCondition = App.EmailSenderCondition.Domain, Sender = "example.invalid" }]
    };
    private static App.EmailNotice Notice(Guid account, uint uid = 10, string subject = "Synthetic important message") => new(App.EmailMessageKey.Inbox(account, 10, uid), "Synthetic sender", "sender@example.invalid", subject, DateTimeOffset.UtcNow, "Synthetic text preview");
    private static void Foundations(Action<bool,string> check)
    {
        var optionalTarget = Settings(); optionalTarget.Account.WebmailUrl = "";
        check(optionalTarget.Account.IsConfigured && !optionalTarget.Account.HasInboxTarget, "Email monitoring accepts no webmail or application target");
        string executable = Environment.ProcessPath!;
        check(App.EmailClientDiscovery.ExecutableFromCommand("\"" + executable + "\" -mail") == executable, "Mail discovery extracts quoted executable without registered arguments");
        check(App.EmailClientDiscovery.ExecutableFromCommand(executable + " -mail") == executable, "Mail discovery extracts unquoted executable path");
        check(App.EmailClientDiscovery.ExecutableFromCommand("https://example.invalid/mail") == null && App.EmailClientDiscovery.ExecutableFromCommand("\"unterminated") == null, "Mail discovery rejects malformed targets");
        check(App.EmailClientDiscovery.ExecutableFromCommand(Path.Combine(Environment.SystemDirectory, "rundll32.exe") + " anything") == null, "Mail discovery does not offer shell helpers as mail clients");
        var installedMail = App.EmailClientDiscovery.Discover();
        check(installedMail.All(client => App.EmailAccountSettings.IsMailProgramPath(client.Path)), "Discovered mail applications have existing executable paths");
        check(installedMail.Select(client => client.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == installedMail.Count && installedMail.Count(client => client.IsDefault) <= 1, "Mail discovery deduplicates registry views and default application");
        Console.WriteLine("Detected mail applications: " + string.Join(", ", installedMail.Select(client => client.Name + (client.IsDefault ? " (Windows default)" : ""))));

        optionalTarget.Account.MailProgramPath = executable;
        check(optionalTarget.Account.IsConfigured && optionalTarget.Account.HasInboxTarget, "Mail executable works without webmail");
        var roundTripTarget = App.EmailWatcherSettings.Parse(JsonSerializer.Serialize(optionalTarget));
        check(roundTripTarget.Account.MailProgramPath == executable && roundTripTarget.Account.WebmailUrl == "", "Mail application target survives serialization");
        check(optionalTarget.Clone().Account.MailProgramPath == executable, "Mail application survives draft cloning");
        check(!App.EmailAccountSettings.IsMailProgramPath("https://example.invalid/app.exe") && !App.EmailAccountSettings.IsMailProgramPath("betterbird.exe"), "Mail application requires an existing absolute executable path");
        optionalTarget.Account.MailProgramPath = Path.Combine(root, "missing.exe");
        check(!optionalTarget.Account.IsConfigured, "Invalid explicit mail application cannot be saved");
        optionalTarget.Account.MailProgramPath = ""; optionalTarget.Account.WebmailUrl = "javascript:alert(1)";
        check(!optionalTarget.Account.IsConfigured, "Optional webmail still rejects unsafe URLs");

        string diagnostic = App.EmailConnectionDiagnostics.Describe(new IOException("private mail and password", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)));
        check(diagnostic.Contains("socket-ConnectionRefused") && diagnostic.Contains("IOException"), "Email diagnostic includes nested socket cause");
        check(!diagnostic.Contains("private mail") && !diagnostic.Contains("password"), "Email diagnostic excludes exception messages");
        check(App.EmailConnectionDiagnostics.Describe(new MailKit.Security.AuthenticationException("secret")).Contains("authentication-failed"), "Email diagnostic identifies authentication failure");
        check(App.EmailConnectionDiagnostics.Describe(new OperationCanceledException("secret")).Contains("timeout"), "Email diagnostic identifies timeout");
        var options = Settings(); string json = JsonSerializer.Serialize(options);
        check(!json.Contains("synthetic-app-secret") && App.WindowsSecretProtector.Unprotect(options.Account.ProtectedPassword) == "synthetic-app-secret", "DPAPI protects app password and decrypts for current user only");
        check(App.WindowsSecretProtector.Unprotect("damaged ciphertext") == "", "Damaged DPAPI credential fails closed");
        check(options.Account.IsConfigured, "Valid secure synthetic account accepted");
        foreach (string url in new[] { "http://webmail.example.invalid", "file:///C:/secret", "javascript:alert(1)", "https://user:secret@webmail.example.invalid", "" })
            check(!App.EmailAccountSettings.IsWebmailUrl(url), "Only credential-free HTTPS webmail links accepted");
        var store = new App.EmailWatcherStateStore(Path.Combine(root, "state")); var state = store.Load(options.Account);
        check(state.Baseline(10, 9) && state.Pending.Count == 0 && state.Through == 9, "Initial UID baseline ignores existing messages");
        state.Process(10, true); state.Process(10, true); state.Process(11, false); store.Save(options.Account.Id, state);
        var restored = store.Load(options.Account);
        check(restored.Through == 11 && restored.Pending.Count == 1 && restored.Pending[0].Uid == 10, "Atomic checkpoint restores pending UID without duplicate IMAP events");
        string path = Path.Combine(root, "state", options.Account.Id.ToString("N") + ".state"); string encrypted = File.ReadAllText(path);
        check(!encrypted.Contains("Fingerprint") && !encrypted.Contains("Pending") && !encrypted.Contains("synthetic-app-secret") && !encrypted.Contains("Synthetic important"), "State contains encrypted identifiers and no mail content");
        check(!restored.Baseline(10, 99) && restored.Pending.Count == 1, "Reconnection retains same epoch checkpoint and pending hints");
        check(restored.Baseline(11, 90) && restored.Pending.Count == 0 && restored.Through == 90, "UIDVALIDITY change safely rebaselines without old-mail alerts");
        File.WriteAllText(path, "corrupt state"); check(!store.Load(options.Account).Initialized, "Corrupt state requests fresh safe baseline");
        store.Save(options.Account.Id, state); options.Account.Server = "changed.example.invalid";
        check(!store.Load(options.Account).Initialized, "Connection identity change cannot reuse another server checkpoint");
        var now = DateTimeOffset.UtcNow; var model = new App.EmailPresentationModel(() => now); var first = Notice(options.Account.Id);
        model.SetBlocked(true); model.Enqueue([first, first]); now += TimeSpan.FromMinutes(5);
        check(model.Next.Length == 0 && model.Deadline == null, "Fullscreen beyond two minutes retains hints without starting auto timer");
        model.SetBlocked(false); var settings = new App.EmailWatcherSettings { DisplayMode = App.EmailWatcherDisplayMode.Automatic };
        check(model.Next.Length == 1, "Fullscreen release bundles duplicate hints into one notice");
        model.Accept(model.Next, settings); check(model.Active.Count == 1 && model.Deadline == now.AddSeconds(15), "Auto deadline begins at actual presentation");
        now += TimeSpan.FromSeconds(10); model.Enqueue([first]); check(model.Next.Length == 0, "Displayed notice replay cannot sound or reset deadline");
        var second = Notice(options.Account.Id, 11); model.Enqueue([second]); model.Accept(model.Next, settings);
        check(model.Deadline == now.AddSeconds(15) && model.Active.Count == 2, "New mail extends auto deadline and preserves previous rows");
        model.SetBlocked(true); now += TimeSpan.FromMinutes(4); check(!model.Expired, "Visible auto deadline pauses when fullscreen begins");
        model.SetBlocked(false); check(!model.Expired, "Fullscreen return preserves remaining auto display time");
        now += TimeSpan.FromSeconds(16); check(model.Expired, "Automatic widget expires after visible-time interval");
        var keys = model.Dismiss(); check(keys.Length == 2 && model.Active.Count == 0, "Dismissal acknowledges all current rows locally");
        model.Enqueue([Notice(options.Account.Id, 12)]); model.Accept(model.Next, new()); now += TimeSpan.FromDays(2);
        check(!model.Expired && model.Active.Count == 1, "Until-closed mode has no expiry and later mail reopens presentation");
        model.Enqueue([Notice(options.Account.Id, 13)]); model.Accept(model.Next, new()); model.DismissKeys([App.EmailMessageKey.Inbox(options.Account.Id, 10, 12)]);
        check(model.Active.Single().Key.Uid == 13, "Concurrent close of old rows preserves newer mail");
        model.Reset(); model.Enqueue(Enumerable.Range(1, 150).Select(i => Notice(options.Account.Id, (uint)i))); model.Accept(model.Next, new());
        check(model.Active.Count == 100 && model.Next.Length == 0, "Presentation memory bounded while excess notices remain pending");
        check(App.EmailText.Limit("a😀e\u0301日本語", 3) == "a😀e\u0301…", "Text limits preserve surrogate pairs and combining characters");
        check(!App.EmailText.Limit("test\0\nnext\tline", 100).Contains('\0'), "Preview removes unsafe control characters");
        foreach (var tone in new[] { App.EmailWatcherSound.Magical, App.EmailWatcherSound.Premium, App.EmailWatcherSound.Fanfare }) {
            using var stream = typeof(App.WidgetSettings).Assembly.GetManifestResourceStream(App.EmailWatcherSoundResources.ResourceName(tone)!);
            byte[] header = new byte[12]; stream?.ReadExactly(header);
            check(stream != null && Encoding.ASCII.GetString(header,0,4) == "RIFF" && Encoding.ASCII.GetString(header,8,4) == "WAVE", "Sound embedded in app assembly: " + tone);
            stream!.Position=0; using var decoder=new System.Media.SoundPlayer(stream); decoder.Load(); check(decoder.IsLoadCompleted,"Embedded PCM WAV loads without playback: "+tone);
        }
        using var sounds = new App.NotificationSounds(); sounds.PlayEmail(App.EmailWatcherSound.None); sounds.PlayEmail((App.EmailWatcherSound)99);
        check(Field<object?>(sounds, "player") == null, "Disabled and unknown mail sounds allocate no playback resources");
        sounds.Dispose(); sounds.PlayEmail(App.EmailWatcherSound.Magical); check(Field<object?>(sounds, "player") == null, "Disposed sound player cannot be restarted");
        foreach (var language in new[] { "de", "en", "fr", "es", "ja" })
            foreach (string key in new[] { "EmailTitle", "EmailUntilClosed", "EmailAutomatic", "EmailPasswordHint", "EmailInvalidRule", "EmailConnected", "EmailCertificateFailed", "EmailOpenInbox", "EmailPrivacyHint", "EmailNoSound" })
                check(App.Localization.Get(key, language) != key, "Localized email text: " + language + "/" + key);
    }
    private static async Task Until(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew(); while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(10);
        if (!condition()) throw new TimeoutException("Synthetic email operation did not finish.");
    }
    private static async Task Service(Action<bool,string> check)
    {
        var options = Settings(); var inbox = new FakeInbox(); var seen = new ConcurrentDictionary<App.EmailMessageKey,App.EmailNotice>();
        var store = new App.EmailWatcherStateStore(Path.Combine(root, "service"));
        using var service = new App.EmailWatcherService(store, () => new FakeMailbox(inbox));
        service.Ready += (_, notices) => { foreach (var notice in notices) seen[notice.Key] = notice; };
        service.Configure(options); await Until(() => inbox.WaitCalls > 0);
        check(seen.Count == 0 && store.Load(options.Account).Through == 9, "Service ignores preexisting inbox on first successful connection");
        inbox.Add(10); await Until(() => seen.Count == 1);
        check(seen.Values.Single().Preview == "synthetic plain preview" && inbox.PreviewCalls == 1, "Matching message loads bounded preview via mailbox adapter");
        inbox.Pulse(); await Until(() => inbox.WaitCalls >= 3);
        check(seen.Count == 1 && inbox.PreviewCalls == 1 && store.Load(options.Account).Pending.Count == 1, "Repeated IMAP notifications do not duplicate or redownload previews");
        inbox.Add(11, "other@else.invalid"); await Until(() => store.Load(options.Account).Through == 11);
        check(seen.Count == 1 && inbox.PreviewCalls == 1, "Nonmatching mail advances checkpoint without body download");
        check(await service.ChangeAsync(service.Generation, seen.Keys.ToArray(), true), "Presentation reserves identifiers before showing notification");
        check(!await service.ChangeAsync(service.Generation, seen.Keys.ToArray(), true), "Already reserved notice cannot be presented twice");
        inbox.FailWait = true; inbox.Pulse(); await Until(() => inbox.Connects >= 2 && inbox.WaitCalls >= 5);
        check(inbox.MaximumActive == 1 && inbox.Active == 1, "Reconnect serializes connections and releases previous mailbox");
        check(store.Load(options.Account).Pending.Any(item=>item.Uid==10&&item.Presented),"Ordinary reconnect retains in-flight display reservation for possible fullscreen requeue");
        service.Dispose(); await service.Completion; check(inbox.Active == 0, "Cancellation releases active mailbox and IDLE wait");
        var restartedSeen = new ConcurrentDictionary<App.EmailMessageKey,App.EmailNotice>();
        using var restart = new App.EmailWatcherService(store, () => new FakeMailbox(inbox)); restart.Ready += (_, notices) => { foreach (var notice in notices) restartedSeen[notice.Key] = notice; };
        restart.Configure(options); await Until(() => inbox.Connects >= 3 && inbox.WaitCalls >= 6);
        check(restartedSeen.Count == 0, "Restart does not reannounce presented message");
        inbox.Add(12); await Until(() => restartedSeen.Count == 1);
        restart.Dispose(); await restart.Completion;
        using var pendingRestart = new App.EmailWatcherService(store, () => new FakeMailbox(inbox)); var restored = new ConcurrentDictionary<App.EmailMessageKey,App.EmailNotice>();
        pendingRestart.Ready += (_, notices) => { foreach (var notice in notices) restored[notice.Key] = notice; };
        pendingRestart.Configure(options); await Until(() => restored.Count == 1);
        check(restored.Keys.Single().Uid == 12, "Restart restores unpresented pending UID without persistent message text");
        inbox.Validity = 20; inbox.Next = 1; inbox.Clear(); inbox.Pulse(); await Until(() => store.Load(options.Account).UidValidity == 20);
        check(store.Load(options.Account).Pending.Count == 0, "Live UIDVALIDITY transition establishes new baseline");
        inbox.Add(1); await Until(() => restored.Keys.Any(key => key.UidValidity == 20));
        check(await pendingRestart.ChangeAsync(pendingRestart.Generation, [App.EmailMessageKey.Inbox(options.Account.Id,10,12)], null), "Dismissal of old epoch does not remove same UID in new epoch");
        pendingRestart.Dispose(); await pendingRestart.Completion;
        var bad = new FakeInbox { RejectLogin = true }; using var authentication = new App.EmailWatcherService(new(Path.Combine(root,"auth")), () => new FakeMailbox(bad));
        string lastStatus = ""; authentication.Status += (_, status) => lastStatus = status; authentication.Configure(options); await authentication.Completion;
        check(bad.Connects == 1 && lastStatus == "EmailAuthenticationFailed" && bad.Active == 0, "Invalid credentials stop rather than retrying rapidly");
        var reconfigured = new FakeInbox(); using var changing = new App.EmailWatcherService(new(Path.Combine(root,"changing")), () => new FakeMailbox(reconfigured));
        var generations = new ConcurrentBag<int>(); changing.Ready += (generation, _) => generations.Add(generation);
        changing.Configure(options); await Until(() => reconfigured.WaitCalls > 0); int old = changing.Generation;
        var next = options.Clone(); next.Account.Server = "other.example.invalid"; changing.Configure(next); await Until(() => reconfigured.Connects == 2 && reconfigured.WaitCalls >= 2);
        check(changing.Generation != old && reconfigured.MaximumActive == 1, "Reconfiguration waits for previous connection to close");
        check(!await changing.ChangeAsync(old, [], null), "Old session callbacks cannot mutate new synchronization state");
        uint largeGap=1_000_000_000; uint receivedGap=0;
        changing.Ready+=(_,notices)=> { if(notices.Any(notice=>notice.Key.Uid==largeGap))receivedGap=largeGap; };
        reconfigured.Add(largeGap); await Until(()=>receivedGap==largeGap);
        check(receivedGap==largeGap,"Sparse large UID gaps are searched directly without numeric-range polling");
        int previewsBefore=reconfigured.PreviewCalls;
        var noPreview=next.Clone(); noPreview.ShowPreview=false; changing.Configure(noPreview); await Until(()=>reconfigured.Connects>=3&&reconfigured.WaitCalls>=4);
        check(reconfigured.PreviewCalls==previewsBefore,"Disabled preview never downloads matching message body on restart");
        changing.Dispose(); await changing.Completion;
    }
    private static void Pump(Func<bool> done)
    {
        var timer = Stopwatch.StartNew(); while (!done() && timer.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(5); }
        if (!done()) throw new TimeoutException("Email UI operation did not finish.");
    }
    private static void Ui(Action<bool,string> check)
    {
        string registry = @"Software\WallpaperControl.EmailTests-" + Guid.NewGuid().ToString("N");
        try {
            var initial = new App.WidgetSettings { EmailWatcher = Settings() }; initial.EmailWatcher.Enabled = false; initial.Save(registry);
            var inbox = new FakeInbox(); using var manager = new App.WidgetManager(() => {}, registryPath:registry, notesStore:new App.NotesStore(Path.Combine(root,"notes.json")), emailMailboxFactory:() => new FakeMailbox(inbox));
            manager.EmailFullscreenState = () => false; manager.Start();
            App.WidgetEditSession? session = null;
            using var editor = new App.WidgetSettingsEditor(manager.Settings, preview:value => session?.Preview(value));
            session = new(manager,editor.ReadWidgetSettings,editor.LoadSettings); session.Begin();
            check(!session.IsDirty && editor.WidgetKeys.Contains("EmailTitle") && inbox.Connects == 0, "Opening email editor neither starts monitoring nor dirties draft");
            var secret = Field<TextBox>(editor,"emailPassword"); secret.Text = "replacement-synthetic-secret";
            Field<TextBox>(editor,"emailName").Text = "Changed draft"; Field<CheckBox>(editor,"emailEnabled").Checked = true;
            check(session.IsDirty && inbox.Connects == 0 && !JsonSerializer.Serialize(manager.Settings).Contains("replacement-synthetic-secret"), "Editing enabled draft never connects and keeps password ciphertext only");
            session.Discard(); check(Field<TextBox>(editor,"emailName").Text == initial.EmailWatcher.Account.DisplayName
                && Field<TextBox>(editor,"emailPassword").Text == "" && manager.Settings.EmailWatcher.Account.ProtectedPassword == initial.EmailWatcher.Account.ProtectedPassword, "Discard restores account, password and enabled state");
            secret.Text="temporary-synthetic-secret"; secret.Clear(); check(!session.IsDirty,"Clearing password input preserves saved ciphertext exactly");
            var rulesList=Field<CheckedListBox>(editor,"emailRulesList"); rulesList.SetItemChecked(0,false);
            check(!editor.ReadWidgetSettings(false).EmailWatcher.Rules[0].Enabled&&session.IsDirty,"Rule checkbox edits enabled state in draft only");
            session.Discard(); check(editor.ReadWidgetSettings(false).EmailWatcher.Rules[0].Enabled,"Discard restores rule activation");
            secret.Text = "replacement-synthetic-secret"; editor.ResetDefaults(); var defaults = editor.ReadWidgetSettings(false).EmailWatcher;
            check(defaults.Account.Id == initial.EmailWatcher.Account.Id && App.WindowsSecretProtector.Unprotect(defaults.Account.ProtectedPassword) == "replacement-synthetic-secret" && defaults.Rules.Count == 1
                && defaults.Sound == App.EmailWatcherSound.Magical && defaults.AutoHideSeconds == 15, "Restore defaults retains draft account, credentials and rules");
            session.Discard(); Field<TextBox>(editor,"emailName").Text = "Saved account";
            var rules = Field<List<App.EmailRule>>(editor,"emailRules"); rules.Add(new() { SubjectContains = "synthetic subject" });
            secret.Text = "replacement-synthetic-secret";
            check(session.Save(), "Save commits account, rules and encrypted credential together");
            var saved = App.WidgetSettings.Load(registry).EmailWatcher;
            check(saved.Rules.Count == 2 && saved.Account.DisplayName == "Saved account" && App.WindowsSecretProtector.Unprotect(saved.Account.ProtectedPassword) == "replacement-synthetic-secret", "Registry round-trip preserves committed email configuration");
            using (var key = Registry.CurrentUser.OpenSubKey(registry)) check(!(key!.GetValue("EmailWatcherConfiguration") as string)!.Contains("replacement-synthetic-secret"), "Registry contains no clear app password");
            int tests = 0; editor.EmailConnectionTest = (account, _) => { tests++; check(account.DisplayName == "Saved account" && account.IsConfigured, "Connection test uses editor values"); return Task.FromResult("EmailConnected"); };
            var test = (Task)typeof(App.WidgetSettingsEditor).GetMethod("TestEmailConnectionAsync", Members)!.Invoke(editor,[])!; Pump(() => test.IsCompleted);
            check(tests == 1 && inbox.Connects == 0 && !session.IsDirty, "Connection test is isolated from production and edit transaction");
            Field<CheckBox>(editor,"emailEnabled").Checked = true; manager.EmailFullscreenState = () => true; int sounds = 0; manager.EmailSoundPlayer = _ => sounds++;
            check(session.Save(), "Saved activation starts monitoring only after commit"); Pump(() => inbox.WaitCalls > 0); inbox.Add(10);
            var service = Field<App.EmailWatcherService>(manager,"emailService"); Pump(() => Field<App.EmailPresentationModel>(manager,"emailPresentation").Next.Length == 0 && service.Generation > 0 && Field<App.EmailPresentationModel>(manager,"emailPresentation").Blocked);
            var waiting = Field<Dictionary<App.EmailMessageKey,App.EmailNotice>>(Field<App.EmailPresentationModel>(manager,"emailPresentation"),"waiting"); Pump(() => waiting.Count > 0);
            check(sounds == 0 && Field<App.EmailWatcherWidgetForm?>(manager,"emailWidget") == null, "Fullscreen blocks widget and sound while IMAP continues independent of wallpaper pause");
            manager.EmailFullscreenState = () => false; Pump(() => Field<App.EmailWatcherWidgetForm?>(manager,"emailWidget")?.Visible == true);
            check(sounds == 1, "Fullscreen release presents one bundle and plays sound once");
            var widget = Field<App.EmailWatcherWidgetForm>(manager,"emailWidget"); widget.Opacity = 0;
            for (int i = 0; i < 5; i++) { using var image = widget.RenderBitmap(); }
            check(sounds == 1, "Redrawing does not replay notification sound");
            var close = (Task)typeof(App.WidgetManager).GetMethod("DismissEmailsAsync",Members)!.Invoke(manager,[])!; Pump(() => close.IsCompleted);
            check(!widget.Visible, "Closing widget locally acknowledges without affecting mailbox"); inbox.Add(11); Pump(() => widget.Visible);
            check(sounds == 2, "Later matching mail reopens widget with one new sound");
            var programChoices = Field<ComboBox>(editor, "emailPrograms");
            var detectedClients = Field<IReadOnlyList<App.EmailClientChoice>>(editor, "emailDetectedClients");
            var programPathBox = Field<TextBox>(editor, "emailMailProgram");
            string oldProgramPath = programPathBox.Text;
            check(programChoices.Items.Count == detectedClients.Count + 2, "Mail selector includes discovered applications, no application and custom selection");
            if (detectedClients.Count > 0) {
                programChoices.SelectedIndex = 1;
                check(programPathBox.Text == detectedClients[0].Path && inbox.Connects == 1, "Selecting detected mail client fills path without connecting IMAP");
            }
            programChoices.SelectedIndex = 0;
            check(programPathBox.Text == "", "No application selection clears custom program path");
            programPathBox.Text = Environment.ProcessPath!;
            check(programChoices.SelectedIndex == programChoices.Items.Count - 1, "Manually chosen application remains available as custom selection");
            programPathBox.Text = oldProgramPath;
            int previews = 0; App.EmailWatcherSound previewTone = App.EmailWatcherSound.None;
            editor.EmailPreviewSoundPlayer = tone => { previews++; previewTone = tone; };
            var previewButton = Field<Button>(editor, "emailSoundPreview");
            Field<ComboBox>(editor, "emailSound").SelectedIndex = 3;
            typeof(Button).GetMethod("OnClick", Members)!.Invoke(previewButton, [EventArgs.Empty]);
            check(previews == 1 && previewTone == App.EmailWatcherSound.Fanfare, "Email sound preview plays current draft selection without real audio");
            Field<ComboBox>(editor, "emailSound").SelectedIndex = 1;
            typeof(Button).GetMethod("OnClick", Members)!.Invoke(previewButton, [EventArgs.Empty]);
            check(previews == 2 && previewTone == App.EmailWatcherSound.Magical && inbox.Connects == 1, "Changing preview tone does not reconnect IMAP");
            Field<ComboBox>(editor,"emailSound").SelectedIndex=0; check(!previewButton.Enabled, "No-sound selection disables preview");
            previewButton.PerformClick(); check(previews == 2, "Disabled email sound preview does not play");
            check(session.Save(),"No-sound selection saved"); inbox.Add(12); Pump(()=>Field<App.EmailPresentationModel>(manager,"emailPresentation").Active.Any(notice=>notice.Key.Uid==12));
            check(sounds==2&&inbox.Connects==1,"No-sound mail display does not play audio or reconnect IMAP");
            App.EmailWatcherSound played=App.EmailWatcherSound.None; manager.EmailSoundPlayer=tone=> { played=tone; sounds++; };
            Field<ComboBox>(editor,"emailSound").SelectedIndex=2; check(session.Save(),"Premium sound selection saved"); inbox.Add(13); Pump(()=>Field<App.EmailPresentationModel>(manager,"emailPresentation").Active.Any(notice=>notice.Key.Uid==13));
            check(played==App.EmailWatcherSound.Premium&&sounds==3,"Configured email-only sound used for next bundle");
            foreach (var language in new[] { "de", "en", "fr", "es", "ja" }) foreach (var style in Enum.GetValues<App.SystemWidgetStyle>()) foreach (bool dark in new[] { false,true }) {
                using var rendered = new App.EmailWatcherWidgetForm(new(){Style=style,MaximumHeight=300}, language, _=>{}, ()=>{}, ()=>{}); rendered.ApplyTheme(dark);
                rendered.SetNotices(Enumerable.Range(1,20).Select(i=>Notice(saved.Account.Id,(uint)i,"Unicode 日本語 😀 " + new string('x',400))).ToArray());
                using var image = rendered.RenderBitmap(1200,144);
                check(image.Width == 585 && image.Height == 450, "Bounded DPI-scaled email list: " + language + "/" + style + "/" + dark);
                if(language=="de"&&style==App.SystemWidgetStyle.Glow&&dark)image.Save(Path.Combine(Path.GetDirectoryName(root)!,"email-widget-preview.png"));
            }
        } finally { Registry.CurrentUser.DeleteSubKeyTree(registry,false); }
    }
    private sealed class FakeInbox
    {
        internal uint Validity = 10, Next = 10;
        internal int Connects, Active, MaximumActive, PreviewCalls, WaitCalls;
        internal bool RejectLogin, FailWait;
        internal readonly ConcurrentDictionary<uint,App.EmailCandidate> Messages = new();
        internal readonly Channel<bool> Signals = Channel.CreateUnbounded<bool>();
        internal void Add(uint uid,string sender="sender@example.invalid") { Messages[uid]=new(uid,"Synthetic sender",[sender],"Synthetic important message",DateTimeOffset.UtcNow); Next=Math.Max(Next,uid+1); Pulse(); }
        internal void Pulse()=>Signals.Writer.TryWrite(true);
        internal void Clear()=>Messages.Clear();
    }
    private sealed class FakeMailbox(FakeInbox data) : App.IEmailMailbox
    {
        private bool opened;
        public uint Validity=>data.Validity;
        public uint NextUid=>data.Next;
        public Task ConnectAsync(App.EmailAccountSettings account,CancellationToken token) {
            token.ThrowIfCancellationRequested(); Interlocked.Increment(ref data.Connects);
            if(data.RejectLogin)throw new MailKit.Security.AuthenticationException();
            opened=true; int count=Interlocked.Increment(ref data.Active); data.MaximumActive=Math.Max(data.MaximumActive,count); return Task.CompletedTask;
        }
        public Task<IReadOnlyList<App.EmailCandidate>> FetchAsync(uint first,uint last,CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<App.EmailCandidate>>(data.Messages.Values.Where(item=>item.Uid>=first&&item.Uid<=last).OrderBy(item=>item.Uid).ToArray()); }
        public Task<uint> HighestAsync(CancellationToken token)=>Task.FromResult(data.Next-1);
        public Task<IReadOnlyList<uint>> NewUidsAsync(uint after,CancellationToken token)=>Task.FromResult<IReadOnlyList<uint>>(data.Messages.Keys.Where(uid=>uid>after).OrderBy(uid=>uid).Take(256).ToArray());
        public Task<App.EmailCandidate?> FindAsync(uint uid,CancellationToken token)=>Task.FromResult(data.Messages.TryGetValue(uid,out var value)?value:null);
        public Task<string> PreviewAsync(uint uid,CancellationToken token) { Interlocked.Increment(ref data.PreviewCalls); return Task.FromResult("synthetic plain preview"); }
        public async Task WaitAsync(int polling,CancellationToken token) { Interlocked.Increment(ref data.WaitCalls); await data.Signals.Reader.ReadAsync(token); if(data.FailWait) { data.FailWait=false; throw new IOException(); } }
        public ValueTask DisposeAsync() { if(opened) { opened=false; Interlocked.Decrement(ref data.Active); } return ValueTask.CompletedTask; }
    }
}
