using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.WinForms;

namespace WallpaperControl;

/// <summary>An opaque child browser in an independent desktop-band Form.</summary>
internal sealed class WebWidgetForm : Form
{
    private readonly WebView2 browser = new() { AllowExternalDrop = false };
    private readonly string profileFolder;
    private readonly Label title = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label status = new() { TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button collapse = new() { FlatStyle = FlatStyle.Flat, TabStop = false };
    private readonly Button reload = new() { FlatStyle = FlatStyle.Flat, Text = "↻", TabStop = false };
    private readonly ToolTip tips = new();
    private readonly CancellationTokenSource lifetime = new();
    private WebWidgetSettings settings = new();
    private string language = "en";
    private string statusKey = "WebLoading";
    private bool initializing;
    private bool applying;
    private bool suspended;
    private bool ready;
    private bool failedProcess;
    private bool closing;
    private bool pendingInitialization;
    private string? pendingUrl;
    internal event EventHandler? GeometrySettled;
    internal WebWidgetSettings Configuration => settings.Clone();
    internal static string UserDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperControl", "WebView2", "default");
    private float ScaleFactor => DeviceDpi / 96f;
    private int Px(int value) => (int)Math.Round(value * ScaleFactor);

    internal WebWidgetForm(WebWidgetSettings value, string language, string? profileFolder = null)
    {
        this.profileFolder = profileFolder ?? UserDataFolder;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        Controls.AddRange([browser, status, title, collapse, reload]);
        collapse.FlatAppearance.BorderSize = 0; reload.FlatAppearance.BorderSize = 0;
        collapse.Click += Collapse_Click; reload.Click += Reload_Click;
        title.MouseDown += Title_MouseDown;
        browser.Enter += Browser_Enter;
        ApplyConfiguration(value, language, true);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { CreateParams value = base.CreateParams; value.ExStyle |= 0x80; return value; }
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        DesktopWidgetNative.AttachToDesktop(this, Location, allowActivation: true);
        if (settings.ReloadOnStartup) _ = InitializeAsync();
        else ShowStatus("WebReady");
    }
    // User clicks may activate this interactive Form. Restore only its Z-order;
    // do not deny activation or force focus during startup/navigation.
    private void Browser_Enter(object? sender, EventArgs e) => QueueDesktopRepair();
    protected override void OnActivated(EventArgs e) { base.OnActivated(e); QueueDesktopRepair(); }
    private void QueueDesktopRepair()
    {
        if (!closing && IsHandleCreated) BeginInvoke((Action)(() => { if (!closing) DesktopWidgetNative.KeepOnDesktop(this); }));
    }
    internal void ApplyConfiguration(WebWidgetSettings value, string newLanguage, bool restoreGeometry)
    {
        bool urlChanged = settings.Url != value.Url;
        WebWidgetSettings next = value.Clone(); next.Normalize();
        if (!restoreGeometry) next.CopyGeometry(settings);
        settings = next; language = newLanguage;
        applying = true;
        try
        {
            ApplyTheme(NotesDialogStyle.ResolveDarkMode());
            title.Text = "⊕  " + (string.IsNullOrWhiteSpace(settings.DisplayName) ? Localization.Get("WebTitle", language) : settings.DisplayName);
            tips.SetToolTip(title, title.Text);
            collapse.Text = (settings.Collapsed ? '＋' : '−').ToString();
            tips.SetToolTip(collapse, Localization.Get(settings.Collapsed ? "WebRestore" : "WebCollapse", language));
            tips.SetToolTip(reload, Localization.Get("WebReload", language));
            collapse.AccessibleName = tips.GetToolTip(collapse); reload.AccessibleName = tips.GetToolTip(reload);
            status.Text = Localization.Get(statusKey, language);
            if (restoreGeometry) RestoreGeometry();
            browser.Enabled = settings.AllowInteraction;
            browser.TabStop = settings.AllowInteraction;
            if (ready) browser.ZoomFactor = settings.Zoom / 100d;
            Arrange();
        }
        finally { applying = false; }
        if (urlChanged && ready) NavigateConfigured();
        else if (urlChanged && IsHandleCreated) _ = InitializeAsync();
    }
    internal void ApplyTheme(bool dark)
    {
        var palette = WidgetDrawing.GetPalette(settings.Style);
        BackColor = dark ? Color.FromArgb(255, palette.panel) : AppTheme.PanelBackground(false);
        ForeColor = dark ? Color.FromArgb(255, palette.text) : AppTheme.TextPrimary(false);
        foreach (Control control in new Control[] { title, status, collapse, reload })
        { control.BackColor = BackColor; control.ForeColor = ForeColor; }
        if (settings.Style == SystemWidgetStyle.Glow)
            collapse.ForeColor = reload.ForeColor = dark ? Color.FromArgb(255, palette.accent) : Color.FromArgb(29, 105, 184);
    }
    private void RestoreGeometry()
    {
        MinimumSize = Size.Empty; MaximumSize = Size.Empty;
        Point position = new(settings.X, settings.Y);
        Rectangle area = Screen.FromPoint(position).WorkingArea;
        Bounds = WebWidgetSettings.ClampBounds(position, new Size(settings.Width, settings.Height), area, ScaleFactor, settings.Collapsed);
        UpdateSizeLimits(area);
    }
    private void UpdateSizeLimits(Rectangle area)
    {
        MinimumSize = new Size(Math.Min(area.Width, Px(320)), Math.Min(area.Height, Px(settings.Collapsed ? 36 : 200)));
        MaximumSize = new Size(area.Width, settings.Collapsed ? Px(36) : area.Height);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e); if (closing) return;
        applying = true;
        try
        {
            settings.X = e.SuggestedRectangle.X; settings.Y = e.SuggestedRectangle.Y;
            RestoreGeometry(); Arrange();
        }
        finally { applying = false; }
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); if (browser != null) Arrange(); }
    private void Arrange()
    {
        int edge = Px(5), header = Px(36), action = Px(30);
        title.SetBounds(edge + Px(5), edge, Math.Max(0, ClientSize.Width - edge * 2 - action * 2 - Px(10)), header - edge * 2);
        reload.SetBounds(ClientSize.Width - edge - action, edge, action, header - edge * 2);
        collapse.SetBounds(reload.Left - action, edge, action, header - edge * 2);
        Rectangle content = new(edge, header, Math.Max(0, ClientSize.Width - edge * 2), Math.Max(0, ClientSize.Height - header - edge));
        // A collapsed browser retains its expanded surface; it is hidden rather than squeezed.
        if (!settings.Collapsed) { browser.Bounds = content; status.Bounds = content; }
        browser.Visible = !settings.Collapsed && ready && statusKey.Length == 0;
        status.Visible = !settings.Collapsed && statusKey.Length != 0;
    }
    private void ShowStatus(string key)
    {
        statusKey = key; status.Text = key.Length == 0 ? "" : Localization.Get(key, language); Arrange();
    }
    private async Task InitializeAsync()
    {
        if (closing || initializing || ready) return;
        if (suspended) { pendingInitialization = true; return; }
        pendingInitialization = false;
        if (!WebWidgetSettings.IsValidUrl(settings.Url)) { ShowStatus("WebInvalidUrl"); return; }
        initializing = true; ShowStatus("WebLoading");
        CancellationToken token = lifetime.Token;
        try
        {
            // Fail in our own localized status surface before the runtime can show
            // its native "cannot create user-data directory" error dialog.
            Directory.CreateDirectory(profileFolder);
            using (FileStream probe = new(Path.Combine(profileFolder, ".write-" + Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, profileFolder).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            await browser.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            CoreWebView2 core = browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.NavigationStarting += NavigationStarting;
            core.NavigationCompleted += NavigationCompleted;
            core.NewWindowRequested += NewWindowRequested;
            core.DownloadStarting += DownloadStarting;
            core.ProcessFailed += ProcessFailed;
            core.PermissionRequested += PermissionRequested;
            ready = true; browser.ZoomFactor = settings.Zoom / 100d;
            NavigateConfigured();
        }
        catch (WebView2RuntimeNotFoundException ex) { ReportFailure("WebRuntimeMissing", ex); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { if (!token.IsCancellationRequested) ReportFailure("WebInitializationFailed", ex); }
        finally { initializing = false; }
    }
    private void NavigateConfigured()
    {
        if (!WebWidgetSettings.IsValidUrl(settings.Url)) { ShowStatus("WebInvalidUrl"); return; }
        if (suspended) { pendingUrl = settings.Url; return; }
        try { ShowStatus("WebLoading"); browser.CoreWebView2.Navigate(settings.Url); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { ReportFailure("WebNavigationFailed", ex); }
    }
    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!WebWidgetSettings.IsValidUrl(e.Uri)) { e.Cancel = true; return; }
        ShowStatus("WebLoading");
    }
    private void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (closing) return;
        if (e.IsSuccess) ShowStatus("");
        else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            ReportFailure("WebNavigationFailed", new InvalidOperationException(e.WebErrorStatus.ToString()));
    }
    private static void NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) => e.Handled = true;
    private static void DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; e.Handled = true; }
    private static void PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e) => e.State = CoreWebView2PermissionState.Deny;
    private void ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        failedProcess = e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited;
        ReportFailure("WebProcessFailed", new InvalidOperationException(e.ProcessFailedKind.ToString()));
    }
    private void ReportFailure(string key, Exception exception)
    {
        AppLogger.Warning("Web widget: " + key, exception);
        if (!closing) ShowStatus(key);
    }
    private void Reload_Click(object? sender, EventArgs e)
    {
        if (suspended || closing) return;
        if (!ready) { _ = InitializeAsync(); return; }
        if (failedProcess) { ShowStatus("WebProcessFailed"); return; }
        try { browser.CoreWebView2.Reload(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { ReportFailure("WebNavigationFailed", ex); }
    }
    private void Collapse_Click(object? sender, EventArgs e) => SetCollapsed(!settings.Collapsed);
    internal void SetCollapsed(bool value)
    {
        if (value == settings.Collapsed) return;
        settings.Collapsed = value;
        // Clear old collapsed maximum/minimum before restoring the expanded bounds.
        MinimumSize = Size.Empty; MaximumSize = Size.Empty;
        ApplyConfiguration(settings, language, true);
        GeometrySettled?.Invoke(this, EventArgs.Empty);
    }
    internal void SetActivitySuspended(bool value)
    {
        suspended = value; reload.Enabled = !value;
        if (!value && pendingInitialization) _ = InitializeAsync();
        if (!value && pendingUrl != null) { pendingUrl = null; if (ready) NavigateConfigured(); }
    }
    private void Title_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || settings.Locked) return;
        title.Capture = false; Capture = false;
        Message message = Message.Create(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); DefWndProc(ref message);
    }
    internal static int ResizeHit(Point point, Size size, int edge, bool locked, bool collapsed)
    {
        if (locked || collapsed) return 1;
        bool left = point.X < edge, right = point.X >= size.Width - edge;
        bool top = point.Y < edge, bottom = point.Y >= size.Height - edge;
        return top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : right ? 11 : 1;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84)
        {
            Point point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            int hit = ResizeHit(point, ClientSize, Px(5), settings.Locked, settings.Collapsed);
            if (hit != 1) { m.Result = new IntPtr(hit); return; }
            if (!settings.Locked && point.Y < Px(36) && point.X < collapse.Left)
            { m.Result = new IntPtr(2); return; }
        }
        if (m.Msg == 0x112 && settings.Locked && (((long)m.WParam & 0xFFF0) is 0xF000 or 0xF010)) return;
        base.WndProc(ref m);
        if (m.Msg == 0x232 && !applying)
        {
            settings.X = Left; settings.Y = Top;
            if (!settings.Collapsed) { settings.Width = (int)Math.Round(Width / ScaleFactor); settings.Height = (int)Math.Round(Height / ScaleFactor); }
            UpdateSizeLimits(Screen.FromControl(this).WorkingArea);
            GeometrySettled?.Invoke(this, EventArgs.Empty); QueueDesktopRepair();
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !closing)
        {
            closing = true; lifetime.Cancel();
            if (ready && browser.CoreWebView2 is { } core)
            {
                core.NavigationStarting -= NavigationStarting; core.NavigationCompleted -= NavigationCompleted;
                core.NewWindowRequested -= NewWindowRequested; core.DownloadStarting -= DownloadStarting;
                core.ProcessFailed -= ProcessFailed; core.PermissionRequested -= PermissionRequested;
            }
            browser.Enter -= Browser_Enter; title.MouseDown -= Title_MouseDown;
            collapse.Click -= Collapse_Click; reload.Click -= Reload_Click;
            GeometrySettled = null;
            browser.Dispose(); title.Dispose(); status.Dispose(); collapse.Dispose(); reload.Dispose(); tips.Dispose(); lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
