namespace WallpaperControl;

/// <summary>Stable IMAP identity, never derived from subject, date, message index or Message-ID.</summary>
internal readonly record struct EmailMessageKey(Guid AccountId, string Folder, uint UidValidity, uint Uid)
{
    internal bool IsValid => AccountId != Guid.Empty && !string.IsNullOrWhiteSpace(Folder)
        && !Folder.Any(char.IsControl) && UidValidity > 0 && Uid > 0;
    internal static EmailMessageKey Inbox(Guid account, uint uidValidity, uint uid) => new(account, "INBOX", uidValidity, uid);
}
