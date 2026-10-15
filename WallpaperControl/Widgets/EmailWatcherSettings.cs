using System.Text.Json;

namespace WallpaperControl;

internal enum EmailWatcherDisplayMode { UntilClosed, Automatic }
internal enum EmailWatcherSound { None, Magical, Premium, Fanfare }
internal enum EmailTransportSecurity { Tls, StartTls }
internal enum EmailAuthenticationKind { AppPassword, OAuth2 }
internal enum EmailSenderCondition { None, Address, Domain }

/// <summary>Non-secret connection preferences. Credentials belong in the later DPAPI store.</summary>
internal sealed class EmailAccountSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Server { get; set; } = "";
    public string DisplayName { get; set; } = "";
    [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.Never)]
    public string ProtectedPassword { get; set; } = "";
    public int Port { get; set; } = 993;
    public EmailTransportSecurity Security { get; set; } = EmailTransportSecurity.Tls;
    public EmailAuthenticationKind Authentication { get; set; } = EmailAuthenticationKind.AppPassword;
    public string Username { get; set; } = "";
    public int PollingSeconds { get; set; } = 60;
    public string WebmailUrl { get; set; } = "";
    public string MailProgramPath { get; set; } = "";
    internal bool HasInboxTarget => !string.IsNullOrWhiteSpace(MailProgramPath) || IsWebmailUrl(WebmailUrl);
    internal EmailAccountSettings Clone() => (EmailAccountSettings)MemberwiseClone();
    internal bool IsConfigured => Id != Guid.Empty && !string.IsNullOrWhiteSpace(Server) && !Server.Any(char.IsControl)
        && Uri.CheckHostName(Server) != UriHostNameType.Unknown && Port is >= 1 and <= 65535
        && Enum.IsDefined(Security) && Authentication == EmailAuthenticationKind.AppPassword
        && !string.IsNullOrWhiteSpace(Username) && !Username.Any(char.IsControl)
        && !string.IsNullOrEmpty(ProtectedPassword)
        && (string.IsNullOrWhiteSpace(WebmailUrl) || IsWebmailUrl(WebmailUrl))
        && (string.IsNullOrWhiteSpace(MailProgramPath) || IsMailProgramPath(MailProgramPath));
    internal static bool IsMailProgramPath(string? value) => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)
        && string.Equals(Path.GetExtension(value), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(value);
    internal static bool IsWebmailUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && !string.IsNullOrWhiteSpace(uri.Host);
}

/// <summary>One sender condition AND an optional subject condition; enabled rules combine with OR.</summary>
internal sealed record EmailRule
{
    public override string ToString()=>"EmailRule (redacted)";
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Enabled { get; init; } = true;
    public EmailSenderCondition SenderCondition { get; init; }
    public string Sender { get; init; } = "";
    public string SubjectContains { get; init; } = "";
}

internal sealed class EmailWatcherSettings
{
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public SystemWidgetStyle Style { get; set; } = SystemWidgetStyle.Glow;
    public Point Location { get; set; } = new(1100, 650);
    public int MaximumHeight { get; set; } = 500;
    public EmailAccountSettings Account { get; set; } = new();
    public List<EmailRule> Rules { get; set; } = [];
    public EmailWatcherDisplayMode DisplayMode { get; set; } = EmailWatcherDisplayMode.UntilClosed;
    public int AutoHideSeconds { get; set; } = 15;
    public bool ShowPreview { get; set; } = true;
    public int PreviewCharacters { get; set; } = 300;
    public bool SoundEnabled { get; set; } = true;
    public EmailWatcherSound Sound { get; set; } = EmailWatcherSound.Magical;
    public bool HoldDuringFullscreen { get; set; } = true;

    internal EmailWatcherSettings Clone()
    {
        var copy = (EmailWatcherSettings)MemberwiseClone();
        copy.Account = Account.Clone();
        copy.Rules = Rules.ToList(); // Rule records are immutable.
        return copy;
    }

    // Only DPAPI ciphertext is included in the serializable account configuration.
    internal static EmailWatcherSettings Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<EmailWatcherSettings>(json) ?? new();
            value.Account ??= new();
            value.Rules = (value.Rules ?? []).Where(rule => rule != null).Select(rule=>rule with { Sender=rule.Sender??"", SubjectContains=rule.SubjectContains??"" }).ToList();
            value.Account.Server??=""; value.Account.Username??=""; value.Account.DisplayName??=""; value.Account.WebmailUrl??=""; value.Account.MailProgramPath??=""; value.Account.ProtectedPassword??="";
            if (!Enum.IsDefined(value.DisplayMode)) value.DisplayMode = EmailWatcherDisplayMode.UntilClosed;
            if (!Enum.IsDefined(value.Style)) value.Style = SystemWidgetStyle.Glow;
            if (!Enum.IsDefined(value.Sound)) value.Sound = EmailWatcherSound.Magical;
            if (!Enum.IsDefined(value.Account.Security)) value.Account.Security = EmailTransportSecurity.Tls;
            if (!Enum.IsDefined(value.Account.Authentication)) value.Account.Authentication = EmailAuthenticationKind.AppPassword;
            if (value.Account.Id == Guid.Empty) value.Account.Id = Guid.NewGuid();
            value.Account.Port = value.Account.Port is >= 1 and <= 65535 ? value.Account.Port : 993;
            value.Account.PollingSeconds = Math.Clamp(value.Account.PollingSeconds, 15, 3600);
            value.AutoHideSeconds = Math.Clamp(value.AutoHideSeconds, 1, 3600);
            value.PreviewCharacters = Math.Clamp(value.PreviewCharacters, 1, 300);
            value.MaximumHeight = CalendarViewport.NormalizeMaximum(value.MaximumHeight);
            return value;
        }
        catch (JsonException) { return new(); }
    }
}

/// <summary>Resource names reserved for the supplied, unmodified WAVs in Phase 5.</summary>
internal static class EmailWatcherSoundResources
{
    internal static string? FileName(EmailWatcherSound sound) => sound switch
    {
        EmailWatcherSound.Magical => "Magical.wav",
        EmailWatcherSound.Premium => "Premium.wav",
        EmailWatcherSound.Fanfare => "Fanfare.wav",
        _ => null
    };
    internal static string? ResourceName(EmailWatcherSound sound) => FileName(sound) is string file
        ? "WallpaperControl.EmailWatcher.Sounds." + file : null;
}
