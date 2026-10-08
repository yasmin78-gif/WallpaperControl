using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace WallpaperControl;
internal sealed class NowPlayingWidgetForm : Form
{
    private const long EmptySessionDelayMilliseconds = 15_000;
    private readonly INowPlayingService service;
    private readonly WidgetDragHandler drag;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly ContextMenuStrip menu = new();
    private readonly ToolTip tooltip = new();
    private NowPlayingSettings settings;
    private string language;
    private bool dark = NotesDialogStyle.ResolveDarkMode(), suspended, powerSuspended, disposed, seeking, desktopReady;
    private string? seekSession;
    private double seekFraction;
    private Bitmap? cover;
    private byte[]? coverBytes;
    private readonly Func<long> monotonicMilliseconds;
    private long? emptySince;
    private bool sessionRecognized;
    internal NowPlayingState DisplayState { get; private set; } = new();
    internal NowPlayingWidgetForm(INowPlayingService service, NowPlayingSettings settings, string language, Action<Point> moved, Func<long>? monotonicMilliseconds = null)
    {
        this.service = service; this.settings = settings.Clone(); this.language = language;
        this.monotonicMilliseconds = monotonicMilliseconds ?? (() => Environment.TickCount64);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None; ClientSize = new(380, 140);
        drag = new(this, () => this.settings.Locked, Render, moved);
        timer.Tick += (_, _) => Tick(); service.Changed += Changed;
        tooltip.SetToolTip(this, Localization.Get("NowPlayingHint", language)); timer.Start();
        Changed();
    }
    protected override bool ShowWithoutActivation => true;
    internal bool ShouldShow => service.State.Unavailable || sessionRecognized && (!settings.HideEmpty || service.State.SessionId != null
        || emptySince == null || monotonicMilliseconds() - emptySince.Value < EmptySessionDelayMilliseconds);
    internal void Tick()
    {
        if (disposed || suspended || powerSuspended) return;
        if (service.State.SessionId == null && !service.State.Unavailable && emptySince != null
            && monotonicMilliseconds() - emptySince.Value >= EmptySessionDelayMilliseconds && DisplayState.SessionId != null) Changed();
        if (!ShouldShow) Hide();
        else if (service.State.Playing && Visible) Render();
    }
    internal void SetDesktopReady(bool value) { desktopReady = value; if (value) Changed(); else Hide(); }
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; return cp; } }
    protected override void WndProc(ref Message m)
    {
        if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
        if (m.Msg == 0x020A && settings.WheelVolume && !suspended && !powerSuspended)
        { NowPlayingVolume.Adjust((short)(m.WParam.ToInt64() >> 16) / 120); m.Result = 0; return; }
        base.WndProc(ref m);
    }
    internal void Apply(NowPlayingSettings value, string lang) { settings = value.Clone(); language = lang; tooltip.SetToolTip(this, Localization.Get("NowPlayingHint", language)); Changed(); }
    internal void ApplyTheme(bool value) { dark = value; Render(); }
    internal void SetActivitySuspended(bool value) { suspended = value; UpdateSuspension(); }
    internal void SetPowerSuspended(bool value) { powerSuspended = value; UpdateSuspension(); }
    private void UpdateSuspension()
    {
        seeking = false; Capture = false; timer.Enabled = !suspended && !powerSuspended;
        if (!timer.Enabled) emptySince = null;
        service.SetSuspended(suspended || powerSuspended); if (timer.Enabled) Changed();
    }
    private void Changed()
    {
        if (disposed || suspended || powerSuspended) return;
        if (service.State.SessionId != null) sessionRecognized = true;
        if (service.State.SessionId != null || service.State.Unavailable) emptySince = null;
        else emptySince ??= monotonicMilliseconds();
        if (service.State.SessionId != null || service.State.Unavailable || DisplayState.SessionId == null
            || emptySince != null && monotonicMilliseconds() - emptySince.Value >= EmptySessionDelayMilliseconds) DisplayState = service.State;
        else DisplayState = DisplayState with
        {
            Position = DisplayState.PositionAt(DateTimeOffset.UtcNow), Updated = DateTimeOffset.UtcNow, Rate = 0,
            Previous = false, Toggle = false, Next = false, Seek = false
        };
        if (seeking && (seekSession != service.State.SessionId || !service.State.Seek)) { seeking = false; Capture = false; }
        if (!ReferenceEquals(coverBytes, DisplayState.Cover))
        {
            cover?.Dispose(); cover = null; coverBytes = DisplayState.Cover;
            if (coverBytes != null)
            {
                try
                {
                    using var stream = new MemoryStream(coverBytes); using var image = Image.FromStream(stream);
                    if (image.Width <= 4096 && image.Height <= 4096) cover = new Bitmap(image, new Size(96,96));
                }
                catch (ArgumentException) { }
                catch (OutOfMemoryException) { }
            }
        }
        if (!ShouldShow) Hide();
        else if (!Visible && IsHandleCreated && desktopReady) { Show(); DesktopWidgetNative.KeepOnDesktop(this); }
        Render();
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Changed(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Render(); }
    private void Render()
    {
        if (disposed || suspended || powerSuspended || !IsHandleCreated || !Visible) return;
        using var bitmap = RenderBitmap(DeviceDpi); ClientSize = bitmap.Size; LayeredWidgetBitmap.Update(Handle, Location, bitmap);
    }
    internal Bitmap RenderBitmap(int dpi = 96)
    {
        var state = DisplayState; bool empty = state.SessionId == null;
        float scale = dpi / 96f; int height = empty ? 62 : 140;
        Bitmap bitmap = new((int)Math.Ceiling(380*scale), (int)Math.Ceiling(height*scale), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap); g.Clear(Color.Transparent); g.ScaleTransform(scale,scale); g.SmoothingMode = SmoothingMode.AntiAlias;
        var palette = WidgetDrawing.GetPalette(settings.Style);
        using var path = WidgetDrawing.RoundedRectangle(new(1,1,378,height-2),12);
        using var fill = new SolidBrush(dark ? palette.panel : Color.FromArgb(235,AppTheme.WindowBackground(false)));
        using var text = new SolidBrush(dark ? palette.text : AppTheme.TextPrimary(false));
        using var muted = new SolidBrush(dark ? palette.muted : AppTheme.TextSecondary(false));
        using var accent = new SolidBrush(dark ? palette.accent : Color.FromArgb(29,105,184));
        using var border = new Pen(accent.Color,settings.Style == SystemWidgetStyle.Glow ? 2 : 1);
        using var font = new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel);
        using var bold = new Font("Segoe UI",13,FontStyle.Bold,GraphicsUnit.Pixel);
        using var control = new Font("Segoe UI Symbol",19,FontStyle.Regular,GraphicsUnit.Pixel);
        using var single = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        g.FillPath(fill,path); g.DrawPath(border,path);
        g.DrawString(Localization.Get("NowPlayingTitle",language),bold,accent,new RectangleF(12,8,290,20),single); g.DrawString("...",bold,accent,350,7);
        if (empty) { g.DrawString(Localization.Get(state.Unavailable ? "NowPlayingUnavailable" : "NowPlayingEmpty",language),font,muted,new RectangleF(12,32,350,20),single); return bitmap; }
        if (cover != null) g.DrawImage(cover,new Rectangle(12,34,64,64)); else g.DrawString("♫",control,accent,31,48);
        g.DrawString(string.IsNullOrWhiteSpace(state.Title) ? NowPlayingService.SourceName(state.Source,language) : state.Title,bold,text,new RectangleF(88,33,278,20),single);
        g.DrawString(state.Artist,font,text,new RectangleF(88,54,278,18),single); g.DrawString(state.Album,font,muted,new RectangleF(88,74,278,18),single);
        g.DrawString("◀◀",font,state.Previous ? text : muted,115,94); g.DrawString(state.Playing ? "❚❚" : "▶",control,state.Toggle ? text : muted,181,89); g.DrawString("▶▶",font,state.Next ? text : muted,244,94);
        if (state.End > state.Start)
        {
            double fraction = seeking ? seekFraction : (state.PositionAt(DateTimeOffset.UtcNow)-state.Start).TotalSeconds/(state.End-state.Start).TotalSeconds;
            using var track = new Pen(muted.Color,3); using var progress = new Pen(accent.Color,3);
            g.DrawLine(track,12,119,368,119); g.DrawLine(progress,12,119,12+(float)Math.Clamp(fraction,0,1)*356,119);
            if (state.Seek) g.FillEllipse(accent,8+(float)Math.Clamp(fraction,0,1)*356,115,8,8);
            var position = seeking ? state.SeekAt(seekFraction) : state.PositionAt(DateTimeOffset.UtcNow);
            g.DrawString(FormatTime(position-state.Start),font,muted,12,123); g.DrawString(FormatTime(state.End-state.Start),font,muted,326,123);
        }
        return bitmap;
    }
    internal static string FormatTime(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{Math.Max(0,(int)time.TotalMinutes):00}:{Math.Max(0,time.Seconds):00}";
    protected override void OnMouseDown(MouseEventArgs e)
    {
        var p = new PointF(e.X*96f/DeviceDpi,e.Y*96f/DeviceDpi);
        if (e.Button == MouseButtons.Right || p.X >= 340 && p.Y < 32) { ShowMenu(e.Location); return; }
        if (suspended || powerSuspended || e.Button != MouseButtons.Left) return;
        var state = service.State;
        if (state.SessionId == null && DisplayState.SessionId != null && p.Y is >= 32 and <= 130) return;
        if (p.Y is >= 111 and <= 130 && state.Seek) { seeking = true; seekSession = state.SessionId; seekFraction = Math.Clamp((p.X-12)/356,0,1); Capture = true; Render(); return; }
        if (p.Y is >= 90 and <= 112 && state.SessionId != null)
        {
            if (p.X is >= 105 and <= 153 && state.Previous) _ = service.CommandAsync(MediaCommand.Previous);
            else if (p.X is >= 170 and <= 212 && state.Toggle) _ = service.CommandAsync(MediaCommand.Toggle);
            else if (p.X is >= 232 and <= 280 && state.Next) _ = service.CommandAsync(MediaCommand.Next);
            return;
        }
        if (p.Y is >= 32 and <= 90 && state.SessionId != null) { FocusSource(); return; }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (seeking) { seekFraction = Math.Clamp((e.X*96f/DeviceDpi-12)/356,0,1); Render(); } else base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (seeking) { var position = service.State.SeekAt(seekFraction); seeking = false; Capture = false; _ = service.CommandAsync(MediaCommand.Seek,position); Render(); return; }
        base.OnMouseUp(e);
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture && seeking) { seeking = false; Render(); } }
    private void ShowMenu(Point point)
    {
        foreach (ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose(); menu.Items.Clear();
        var automatic = new ToolStripMenuItem(Localization.Get("NowPlayingAutomatic",language)) { Checked = service.Selected == null }; automatic.Click += (_,_) => service.Select(null); menu.Items.Add(automatic);
        foreach (var choice in service.Sessions)
        {
            string name = choice.Name;
            if (choice.Id.Contains('\u001f'))
            {
                string source = choice.Id.Split('\u001f')[0];
                name = choice.Name.Replace(NowPlayingService.SourceName(source),NowPlayingService.SourceName(source,language),StringComparison.Ordinal);
            }
            var item = new ToolStripMenuItem(name) { Checked = choice.Id == service.Selected }; item.Click += (_,_) => service.Select(choice.Id); menu.Items.Add(item);
        }
        ContextMenuTheme.Apply(menu,dark); menu.Show(this,point);
    }
    private void FocusSource()
    {
        var source = service.State.Source;
        string name = NowPlayingService.SourceName(source);
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_')) return;
        var processes = Process.GetProcessesByName(name);
        try
        {
            foreach (var process in processes)
            {
                try { var window = process.MainWindowHandle; if (window != IntPtr.Zero) { if (IsIconic(window)) ShowWindow(window,9); SetForegroundWindow(window); return; } }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window,int command);
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; service.Changed -= Changed; timer.Dispose(); drag.Dispose(); cover?.Dispose(); coverBytes = null; DisplayState = new(); tooltip.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}
