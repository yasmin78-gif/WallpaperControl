using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace WallpaperControl;

internal static class EmailRuleMatcher
{
    /// <summary>One decision per message, even when several rules or sender mailboxes match.</summary>
    internal static bool Matches(IEnumerable<EmailRule> rules, IEnumerable<string> senderAddresses, string? subject)
    {
        var senders = senderAddresses.Select(address => TryNormalizeAddress(address, out var normalized) ? normalized : null)
            .Where(address => address != null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string normalizedSubject;
        try { normalizedSubject = (subject ?? "").Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
        foreach (var rule in rules)
        {
            if (rule == null || !rule.Enabled || !IsValid(rule)) continue;
            if (!string.IsNullOrEmpty(rule.SubjectContains) && !normalizedSubject.Contains(
                rule.SubjectContains.Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase)) continue;
            if (rule.SenderCondition == EmailSenderCondition.None) return true;
            if (rule.SenderCondition == EmailSenderCondition.Address && TryNormalizeAddress(rule.Sender, out var exact)
                && senders.Contains(exact, StringComparer.OrdinalIgnoreCase)) return true;
            if (rule.SenderCondition == EmailSenderCondition.Domain && TryNormalizeDomain(rule.Sender, out var domain)
                && senders.Any(address => address[(address.LastIndexOf('@') + 1)..].Equals(domain, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    internal static bool IsValid(EmailRule rule)
    {
        if (!Enum.IsDefined(rule.SenderCondition) || rule.Sender == null || rule.SubjectContains == null) return false;
        try { _ = rule.SubjectContains.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
        bool subject = !string.IsNullOrWhiteSpace(rule.SubjectContains);
        if (!subject && rule.SubjectContains.Length != 0) return false;
        return rule.SenderCondition switch
        {
            EmailSenderCondition.None => subject && string.IsNullOrWhiteSpace(rule.Sender),
            EmailSenderCondition.Address => TryNormalizeAddress(rule.Sender, out _),
            EmailSenderCondition.Domain => TryNormalizeDomain(rule.Sender, out _),
            _ => false
        };
    }

    /// <summary>Accept exactly one bare mailbox; no display names, lists or header injection.</summary>
    internal static bool TryNormalizeAddress(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input) || input.Any(char.IsControl)) return false;
        try
        {
            string value = input.Trim().Normalize(NormalizationForm.FormC);
            if (!MailAddress.TryCreate(value, out var address) || address.DisplayName.Length != 0
                || !value.Equals(address.Address, StringComparison.OrdinalIgnoreCase)
                || !TryNormalizeDomain(address.Host, out var domain)) return false;
            normalized = address.User + "@" + domain;
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Exact DNS domain, with optional leading @ for user-entered domain rules.</summary>
    internal static bool TryNormalizeDomain(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input) || input.Any(char.IsControl)) return false;
        try
        {
            string value = input.Trim();
            if (value.StartsWith('@')) value = value[1..];
            value = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(value.Normalize(NormalizationForm.FormC)).ToLowerInvariant();
            if (value.Length > 253) return false;
            var labels = value.Split('.');
            if (labels.Length < 2 || labels.Any(label => label.Length is < 1 or > 63
                || label[0] == '-' || label[^1] == '-' || label.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))) return false;
            normalized = value;
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
