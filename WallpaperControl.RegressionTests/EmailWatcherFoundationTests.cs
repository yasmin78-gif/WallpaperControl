extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Text.Json;

internal static class EmailWatcherFoundationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var settings = new App.EmailWatcherSettings();
        check(!settings.Enabled && settings.DisplayMode == App.EmailWatcherDisplayMode.UntilClosed, "Email watcher starts disabled, until closed mode selected");
        check(settings.AutoHideSeconds == 15 && settings.ShowPreview && settings.PreviewCharacters == 300, "Display defaults: 15 seconds and 300 character enabled preview");
        check(settings.SoundEnabled && settings.Sound == App.EmailWatcherSound.Magical && settings.HoldDuringFullscreen, "Magical sound and fullscreen hold enabled by default");
        check(settings.Account.Port == 993 && settings.Account.Security == App.EmailTransportSecurity.Tls
            && settings.Account.Authentication == App.EmailAuthenticationKind.AppPassword && settings.Account.PollingSeconds == 60, "Secure account defaults and 60 second polling");
        check(settings.Account.Id != Guid.Empty && settings.Rules.Count == 0, "Stable account identity and no default match-all rules");
        check(Enum.GetValues<App.EmailWatcherDisplayMode>().Length == 2 && Enum.GetValues<App.EmailTransportSecurity>().Length == 2, "Only two display modes and no plaintext transport option");

        var address = new App.EmailRule { SenderCondition = App.EmailSenderCondition.Address, Sender = "Sender@Example.Invalid" };
        var domain = new App.EmailRule { SenderCondition = App.EmailSenderCondition.Domain, Sender = "@example.invalid" };
        var subject = new App.EmailRule { SubjectContains = "Important" };
        bool Match(App.EmailRule rule, string sender = "sender@example.invalid", string? text = "important update") => App.EmailRuleMatcher.Matches([rule], [sender], text);
        check(Match(address, "SENDER@EXAMPLE.INVALID"), "Exact mailbox comparison ignores case");
        check(!Match(address, "other@example.invalid"), "Exact mailbox rejects different user");
        check(!Match(address, "sender@example.invalid.evil.invalid"), "Exact mailbox rejects suffix impersonation");
        check(Match(address with { Sender = "  sender@example.invalid  " }), "Outer rule whitespace is normalized");
        check(Match(domain), "Domain rule accepts bare parsed sender mailbox");
        foreach (string hostile in new[] { "sender@example.invalid.evil.invalid", "sender@notexample.invalid", "sender@sub.example.invalid", "example.invalid@evil.invalid" })
            check(!Match(domain, hostile), "Domain compares actual complete host: " + hostile);
        check(!Match(domain, "Example.invalid <sender@evil.invalid>"), "Display name cannot impersonate domain");
        check(Match(domain, "\"local@other.invalid\"@example.invalid"), "Quoted local @ does not change actual sender domain");
        check(Match(subject, "different@else.invalid", "VERY IMPORTANT notice"), "Subject condition is case insensitive and independent of sender");
        check(!Match(subject, text: "ordinary update") && !Match(subject, text: null), "Subject rule rejects missing and nonmatching subject");
        check(Match(subject, "invalid mailbox"), "Subject-only rule does not require a valid sender mailbox");
        var combination = address with { SubjectContains = "important" };
        check(Match(combination), "Combined rule accepts sender AND subject");
        check(!Match(combination, "other@example.invalid") && !Match(combination, text: "ordinary"), "Combined rule requires both conditions");
        check(Match(domain with { SubjectContains = "important" }) && !Match(domain with { SubjectContains = "important" }, text: "ordinary"), "Domain plus subject also uses AND");
        check(App.EmailRuleMatcher.Matches([address with { Sender = "other@else.invalid" }, subject], ["sender@example.invalid"], "important"), "Enabled rules combine with OR");
        check(App.EmailRuleMatcher.Matches([address, domain, subject, address], ["sender@example.invalid", "SENDER@example.invalid"], "important"), "Multiple rules and duplicate senders produce a single boolean decision");
        check(!Match(address with { Enabled = false }) && !App.EmailRuleMatcher.Matches([], ["sender@example.invalid"], "important"), "Disabled or absent rules never match");
        check(App.EmailRuleMatcher.Matches([address], ["other@else.invalid", "sender@example.invalid"], ""), "Any parsed From mailbox may satisfy sender condition");
        check(!App.EmailRuleMatcher.Matches([domain], [], "important"), "Sender rule rejects empty From mailbox list");
        check(Match(subject with { SubjectContains = "Café" }, text: "CAFE\u0301 report"), "Subject Unicode normalization preserves equivalent accents");
        check(Match(domain with { Sender = "@bücher.invalid" }, "sender@xn--bcher-kva.invalid"), "IDN domain rule matches its ASCII representation");
        check(Match(address with { Sender = "sender@bücher.invalid" }, "sender@xn--bcher-kva.invalid"), "Exact address normalizes international domain");
        check(Match(subject with { SubjectContains = "重要" }, text: "新しい重要な連絡"), "Non-Latin subject text matches without ASCII conversion");
        check(!Match(subject with { SubjectContains = "Cafe" }, text: "Café"), "Normalization does not strip meaningful accents");

        foreach (string invalid in new[] { "", " ", "@@example.invalid", "example.invalid:993", "https://example.invalid", "*.example.invalid", "example..invalid", "-example.invalid", "example-.invalid", "localhost", "example.invalid.", "example.invalid\r\n", "[127.0.0.1]", "bad_name.invalid", new string('a',64)+".invalid", "\ud800.invalid" })
            check(!App.EmailRuleMatcher.TryNormalizeDomain(invalid, out _), "Invalid domain rejected safely: " + invalid.Replace('\r', '_').Replace('\n', '_'));
        foreach (string invalid in new[] { "", "sender", "sender@", "Name <sender@example.invalid>", "sender@example.invalid,other@example.invalid", "sender@example.invalid\r\nBcc:other@example.invalid", "sender@bad_host.invalid", "sender@example.invalid (comment)", "\ud800@example.invalid" })
            check(!App.EmailRuleMatcher.TryNormalizeAddress(invalid, out _), "Invalid bare address rejected safely");
        check(!App.EmailRuleMatcher.IsValid(new()) && !Match(new()), "Empty rule never matches everything");
        check(!Match(subject with { SubjectContains = " " }) && !Match(subject with { SubjectContains = "\ud800" }), "Whitespace and malformed Unicode rules do not match");
        check(!Match(subject with { SenderCondition = (App.EmailSenderCondition)99 }), "Unknown rule type fails closed");
        check(!Match(subject with { Sender = "hidden@example.invalid" }), "Subject-only rule rejects ambiguous extra sender condition");
        check(!App.EmailRuleMatcher.Matches([subject], ["sender@example.invalid"], "\ud800"), "Malformed subject does not throw");
        check(App.EmailRuleMatcher.TryNormalizeDomain("  @EXAMPLE.INVALID  ", out var normalized) && normalized == "example.invalid", "Domain normalization is deterministic");

        settings.Rules.Add(address);
        var clone = settings.Clone(); clone.Account.Server = "imap.example.invalid"; clone.Rules[0] = address with { Enabled = false }; clone.Rules.Add(subject);
        check(settings.Account.Server == "" && settings.Rules.Count == 1 && settings.Rules[0].Enabled, "Clone isolates mutable account and rule list");
        check(clone.Account.Id == settings.Account.Id && clone.Rules[0].Id == address.Id, "Clone preserves stable account and rule identifiers");
        var global = new App.WidgetSettings { EmailWatcher = settings }; var globalClone = global.Clone(); globalClone.EmailWatcher.Account.Port = 143;
        check(global.EmailWatcher.Account.Port == 993, "Existing WidgetSettings clone deeply includes email foundations");
        settings.Account.Username = "synthetic@example.invalid";
        string json = JsonSerializer.Serialize(settings); var restored = App.EmailWatcherSettings.Parse(json);
        check(restored.Account.Id == settings.Account.Id && restored.Account.Username == settings.Account.Username
            && restored.Rules.Single() == address && restored.Sound == App.EmailWatcherSound.Magical, "Non-secret settings serialization round-trip preserves identifiers and rules");
        check(!typeof(App.EmailAccountSettings).GetProperties().Any(property=>property.Name is "Password" or "Token" or "AccessToken") && restored.Account.ProtectedPassword=="", "Settings model has no plaintext credential payload");
        foreach (string input in new[] { "{}", "null", "{broken", "[]" })
            check(App.EmailWatcherSettings.Parse(input).AutoHideSeconds == 15, "Missing or damaged settings return safe defaults");
        var repaired = App.EmailWatcherSettings.Parse("{\"Account\":null,\"Rules\":null,\"DisplayMode\":99,\"Sound\":99,\"Style\":99,\"PreviewCharacters\":9000,\"AutoHideSeconds\":0}");
        check(repaired.Account != null && repaired.Rules.Count == 0 && repaired.DisplayMode == App.EmailWatcherDisplayMode.UntilClosed
            && repaired.Sound == App.EmailWatcherSound.Magical && repaired.PreviewCharacters == 300 && repaired.AutoHideSeconds == 1, "Persisted settings are normalized without enabling monitoring");
        repaired = App.EmailWatcherSettings.Parse("{\"Account\":{\"Port\":0,\"Security\":99,\"Authentication\":99,\"PollingSeconds\":0}}");
        check(repaired.Account.Port == 993 && repaired.Account.Security == App.EmailTransportSecurity.Tls && repaired.Account.PollingSeconds == 15, "Invalid connection values normalize to secure bounded values");

        var key = App.EmailMessageKey.Inbox(settings.Account.Id, 10, 20);
        check(key.IsValid && key == App.EmailMessageKey.Inbox(settings.Account.Id, 10, 20), "IMAP identity is stable");
        check(key != key with { AccountId = Guid.NewGuid() } && key != key with { Folder = "Other" }
            && key != key with { UidValidity = 11 } && key != key with { Uid = 21 }, "All four IMAP identity components distinguish messages");
        check(!default(App.EmailMessageKey).IsValid && !(key with { Uid = 0 }).IsValid && !(key with { UidValidity = 0 }).IsValid
            && !(key with { Folder = "INBOX\n" }).IsValid, "Invalid IMAP identities are recognizable");
        foreach (var tone in new[] { App.EmailWatcherSound.Magical, App.EmailWatcherSound.Premium, App.EmailWatcherSound.Fanfare })
            check(App.EmailWatcherSoundResources.FileName(tone) == tone + ".wav"
                && App.EmailWatcherSoundResources.ResourceName(tone) == "WallpaperControl.EmailWatcher.Sounds." + tone + ".wav", "Supplied sound names reserved for later embedding: " + tone);
        check(App.EmailWatcherSoundResources.ResourceName(App.EmailWatcherSound.None) == null
            && App.EmailWatcherSoundResources.ResourceName((App.EmailWatcherSound)99) == null, "No sound and unknown sound do not resolve resource names");
    }
}
