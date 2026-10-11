namespace WallpaperControl;

// WinForms disables all visible thread windows for ShowDialog, even ownerless desktop widgets.
// This exception applies only to the existing clipboard window, after modal setup is complete.
internal sealed class ClipboardModalInteraction : IDisposable
{
    private readonly ClipboardWidgetForm widget;
    private readonly Func<bool> canInteract;
    private bool queued,disposed;
    internal bool IsModal { get; private set; }
    internal ClipboardModalInteraction(ClipboardWidgetForm widget,Func<bool> canInteract)
    { this.widget=widget; this.canInteract=canInteract; IsModal=Application.OpenForms.Cast<Form>().Any(form=>form.Modal&&!form.InvokeRequired); Application.EnterThreadModal+=OnEnterModal; Application.LeaveThreadModal+=OnLeaveModal; widget.VisibleChanged+=OnVisibleChanged; }
    private void OnEnterModal(object? sender,EventArgs e) { IsModal=true; Schedule(); }
    private void OnLeaveModal(object? sender,EventArgs e)=>IsModal=false;
    private void OnVisibleChanged(object? sender,EventArgs e) { if(IsModal&&widget.Visible)Schedule(); }
    internal void Refresh() { if(IsModal)Schedule(); }
    internal void WindowDisabled()
    {
        // EnterThreadModal covers the outer loop. Nested loops disable the window again
        // without raising that event, so observe only this window's WM_ENABLE as well.
        if(IsModal)Schedule();
    }
    private void Schedule()
    {
        if(disposed||queued||!widget.IsHandleCreated)return;
        queued=true;
        widget.BeginInvoke((Action)(()=>
        {
            queued=false;
            if(!disposed&&!widget.IsDisposed&&widget.IsHandleCreated&&widget.Visible&&widget.Enabled&&canInteract()&&IsModal)
                DesktopWidgetNative.EnableInteraction(widget);
        }));
    }
    public void Dispose()
    { if(disposed)return; disposed=true; Application.EnterThreadModal-=OnEnterModal; Application.LeaveThreadModal-=OnLeaveModal; widget.VisibleChanged-=OnVisibleChanged; }
}
