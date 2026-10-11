namespace WallpaperControl;

internal static class ClipboardDiagnostics
{
    internal static void Failure(string stage,Exception error)
    {
        // Only image/IO errors reach here. Never log clipboard text, image bytes or source metadata.
        string reason=error is System.Text.Json.JsonException json? $"invalid index JSON at line {json.LineNumber}, byte {json.BytePositionInLine}":error.Message;
        reason=reason.Replace('\r',' ').Replace('\n',' '); if(reason.Length>300)reason=reason[..300];
        AppLogger.Info($"Clipboard screenshots: stage={stage}; error={error.GetType().Name}; hresult=0x{error.HResult:X8}; reason={reason}");
    }
}
