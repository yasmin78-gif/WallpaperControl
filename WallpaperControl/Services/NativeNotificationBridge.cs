using System.Reflection;
using System.Runtime.InteropServices;

namespace WallpaperControl;

/// <summary>WinForms does not expose NIIF_NOSOUND. Isolate the .NET 10 tray identity bridge here.</summary>
internal sealed class NativeNotificationBridge : NativeWindow, IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Data
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id, Flags, Callback;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Title;
        internal uint InfoFlags;
        internal Guid Guid;
        internal IntPtr BalloonIcon;
    }
    private static readonly FieldInfo? WindowField = typeof(NotifyIcon).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? IdField = typeof(NotifyIcon).GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly NotifyIcon icon;
    private readonly Action shown;
    private readonly Action? hidden;
    private readonly Func<Data, bool> send;
    private Action? posted;
    private bool disposed;
    internal NativeNotificationBridge(NotifyIcon icon, Action shown, Func<Data, bool>? send = null, Action? hidden = null)
    { this.icon = icon; this.shown = shown; this.hidden = hidden; this.send = send ?? (data => ShellNotifyIcon(1, ref data)); }
    internal void Post(Action action)
    {
        if (disposed) return;
        if (Handle == IntPtr.Zero) { action(); return; }
        if (posted != null) return;
        posted = action;
        if (!PostMessage(Handle, 0x8002, IntPtr.Zero, IntPtr.Zero)) { posted = null; action(); }
    }
    internal bool Show(string title, string body, bool silent)
    {
        if (disposed || !icon.Visible || WindowField?.GetValue(icon) is not NativeWindow window
            || window.Handle == IntPtr.Zero || IdField?.GetValue(icon) is not uint id) return false;
        if (Handle != window.Handle) { if (Handle != IntPtr.Zero) ReleaseHandle(); AssignHandle(window.Handle); }
        Data data = new()
        {
            Size = (uint)Marshal.SizeOf<Data>(), Window = window.Handle, Id = id,
            Flags = 0x10, Tip = "", Info = body.Length > 255 ? body[..255] : body,
            Title = title.Length > 63 ? title[..63] : title, Timeout = 10_000,
            InfoFlags = 1u | 0x80u | (silent ? 0x10u : 0u)
        };
        return send(data);
    }
    protected override void WndProc(ref Message message)
    {
        // NotifyIcon's public Shown event also fires for NIN_BALLOONHIDE; use the actual SHOW message only.
        if (message.Msg == 0x8002)
        {
            var action = posted; posted = null; if (!disposed) action?.Invoke();
            message.Result = IntPtr.Zero; return;
        }
        if (message.Msg == 0x800 && !disposed)
        {
            if (message.LParam.ToInt64() == 0x402) shown();
            else if (message.LParam.ToInt64() == 0x403) hidden?.Invoke();
        }
        base.WndProc(ref message);
    }
    public void Dispose() { if (disposed) return; disposed = true; posted = null; if (Handle != IntPtr.Zero) ReleaseHandle(); }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref Data data);
}
