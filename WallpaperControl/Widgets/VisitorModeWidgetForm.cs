namespace WallpaperControl;

internal sealed class VisitorModeWidgetForm : Form
{
    private readonly VisitorModeService service;
    private readonly VisitorToggleSwitch toggle = new();
    private readonly Label status = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly WidgetDragHandler drag;
    private readonly ToolTip tooltip = new();
    private bool updating;
    internal VisitorModeWidgetForm(VisitorModeService service, Action<Point> moved)
    {
        this.service = service;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(248, 72); BackColor = Color.FromArgb(32, 38, 44); ForeColor = Color.White;
        toggle.Location = new Point(162, 20); toggle.Size = new Size(76, 34); toggle.BackColor = BackColor;
        status.SetBounds(12, 10, 145, 52); status.ForeColor = ForeColor;
        Controls.Add(status); Controls.Add(toggle);
        // Drag only the background; clicking the switch never moves the widget.
        drag = new WidgetDragHandler(this, () => service.Settings.WidgetLocked, Invalidate, moved);
        status.MouseDown += (_, e) => OnMouseDown(e);
        status.MouseMove += (_, e) => OnMouseMove(e);
        status.MouseUp += (_, e) => OnMouseUp(e);
        toggle.CheckedChanged += async (_, _) => { if (!updating) await service.SetActiveAsync(toggle.Checked); };
        service.Changed += RefreshState;
        RefreshState();
    }
    internal void RefreshState()
    {
        if (IsDisposed) return;
        updating = true;
        try
        {
            toggle.Checked = service.Active; toggle.Enabled = !service.Busy;
            toggle.AccessibleName = Localization.Get("VisitorTitle");
            status.Text = Localization.Get("VisitorTitle") + Environment.NewLine +
                Localization.Get(service.Busy ? "VisitorSwitching" : service.Error != null ? "VisitorFallback" : service.Active ? "VisitorOn" : "VisitorOff");
            AccessibleDescription = Localization.Get(service.Error ?? (service.Active ? "VisitorActive" : "VisitorInactive"));
            tooltip.SetToolTip(status, AccessibleDescription);
        }
        finally { updating = false; }
    }
    protected override void WndProc(ref Message m)
    {
        if (DesktopWidgetNative.HandleMouseActivation(ref m)) return;
        base.WndProc(ref m);
    }
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { service.Changed -= RefreshState; drag.Dispose(); tooltip.Dispose(); }
        base.Dispose(disposing);
    }
}
