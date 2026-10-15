namespace WallpaperControl;

/// <summary>Deliberately excludes exception messages, server responses and account data.</summary>
internal static class EmailConnectionDiagnostics
{
    internal static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (Exception? current = exception; current != null && parts.Count < 6; current = current.InnerException)
        {
            string reason = current switch {
                MailKit.Security.AuthenticationException => "authentication-failed",
                MailKit.Security.SslHandshakeException => "tls-handshake-failed",
                System.Security.Authentication.AuthenticationException => "tls-validation-failed",
                OperationCanceledException or TimeoutException => "timeout",
                System.Net.Sockets.SocketException socket => "socket-" + socket.SocketErrorCode,
                System.Security.Cryptography.CryptographicException => "credential-or-state-decryption-failed",
                UnauthorizedAccessException => "access-denied",
                IOException => "network-or-storage-io-failed",
                _ => "operation-failed"
            };
            parts.Add(current.GetType().Name + ":" + reason + ":hresult=" + current.HResult.ToString("X8"));
        }
        return string.Join(" -> ", parts);
    }

    internal static void LogFailure(Exception exception, string phase, int attempt, bool retry) =>
        AppLogger.Info($"Email watcher: attempt={attempt}; phase={phase}; failed; retry={retry}; cause={Describe(exception)}");
}
