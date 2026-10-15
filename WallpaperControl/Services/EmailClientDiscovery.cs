using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperControl;

internal sealed record EmailClientChoice(string Name, string Path, bool IsDefault);

internal static class EmailClientDiscovery
{
    internal static IReadOnlyList<EmailClientChoice> Discover()
    {
        var choices = new List<EmailClientChoice>();
        string? associatedPath = QueryAssociation(2);
        string? defaultPath = associatedPath == null ? null : ExecutableFromCommand("\"" + associatedPath + "\"");
        if (EmailAccountSettings.IsMailProgramPath(defaultPath))
            choices.Add(new(QueryAssociation(4) ?? System.IO.Path.GetFileNameWithoutExtension(defaultPath!), defaultPath!, true));
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var mail = root.OpenSubKey(@"Software\Clients\Mail");
                if (mail == null) continue;
                foreach (string name in mail.GetSubKeyNames())
                {
                    using var client = mail.OpenSubKey(name);
                    using var command = client?.OpenSubKey(@"shell\open\command");
                    string? path = ExecutableFromCommand(command?.GetValue("") as string);
                    if (path == null || choices.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                    choices.Add(new(client?.GetValue("") as string is { Length: > 0 } label ? label : name, path, false));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            { AppLogger.Info("Email client discovery unavailable: " + ex.GetType().Name); }
        }
        return choices.OrderByDescending(item => item.IsDefault).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    // Extract only a validated executable. Never run a registered command line or mailto compose arguments.
    internal static string? ExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        string path;
        if (command[0] == '"') {
            int end = command.IndexOf('"', 1);
            if (end < 0) return null;
            path = command[1..end];
        }
        else {
            int end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end < 0 || end + 4 < command.Length && !char.IsWhiteSpace(command[end + 4])) return null;
            path = command[..(end + 4)];
        }
        if (!EmailAccountSettings.IsMailProgramPath(path)) return null;
        if (new[] { "rundll32.exe", "explorer.exe", "cmd.exe", "powershell.exe", "pwsh.exe" }.Contains(System.IO.Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)) return null;
        return path;
    }

    private static string? QueryAssociation(uint kind)
    {
        uint length = 0;
        // ASSOCF_IS_PROTOCOL: ask Windows for the current mailto owner, without launching it.
        AssocQueryString(0x1000, kind, "mailto", null, null, ref length);
        if (length is 0 or > 32768) return null;
        var text = new StringBuilder((int)length);
        return AssocQueryString(0x1000, kind, "mailto", null, text, ref length) == 0 ? text.ToString() : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, uint kind, string association, string? extra, StringBuilder? output, ref uint length);
}
