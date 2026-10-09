namespace WallpaperControl;
/// <summary>UI-thread registry connecting every drag path to its manager's visible widgets.</summary>
internal static class WidgetSnapping
{
    private sealed class Registration(WidgetSnapCoordinator coordinator)
    { internal WidgetSnapCoordinator Coordinator=coordinator; internal WidgetSnapEngine Engine=new(); internal bool Dragging,NativeMotion; }
    private static readonly Dictionary<Control,Registration> registrations=[];
    internal static void Register(Control widget,WidgetSnapCoordinator coordinator)
    {
        if(registrations.ContainsKey(widget))return;
        registrations.Add(widget,new(coordinator)); widget.Disposed+=Removed; widget.VisibleChanged+=VisibilityChanged; widget.MouseCaptureChanged+=CaptureChanged;
    }
    private static void Removed(object? sender,EventArgs e) { if(sender is Control widget)Unregister(widget); }
    private static void VisibilityChanged(object? sender,EventArgs e) { if(sender is Control widget&&!widget.Visible)End(widget); }
    private static void CaptureChanged(object? sender,EventArgs e) { if(sender is Control widget&&!widget.Capture&&registrations.TryGetValue(widget,out var registration)&&!registration.NativeMotion)End(widget); }
    internal static void Unregister(Control widget)
    { End(widget); registrations.Remove(widget); widget.Disposed-=Removed; widget.VisibleChanged-=VisibilityChanged; widget.MouseCaptureChanged-=CaptureChanged; }
    internal static void Begin(Control widget,bool nativeMotion=false) { if(registrations.TryGetValue(widget,out var registration)) { registration.Engine.Reset(); registration.Dragging=true; registration.NativeMotion=nativeMotion; } }
    internal static Point Move(Control widget,Point proposed)
    {
        if(!registrations.TryGetValue(widget,out var registration)||!registration.Dragging)return proposed;
        return registration.Coordinator.Move(widget,proposed,registration.Engine,(Control.ModifierKeys&Keys.Alt)!=0);
    }
    internal static void End(Control widget)
    { if(registrations.TryGetValue(widget,out var registration)) { registration.Dragging=false; registration.NativeMotion=false; registration.Engine.Reset(); registration.Coordinator.Clear(); } }
}
internal sealed class WidgetSnapCoordinator(Func<IEnumerable<Form>> widgets,bool enabled) : IDisposable
{
    private SnapGuideForm? vertical,horizontal;
    internal bool Enabled { get; private set; }=enabled;
    internal void SetEnabled(bool value) { Enabled=value; if(!value)Clear(); }
    internal Point Move(Control widget,Point proposed,WidgetSnapEngine engine,bool bypass)
    {
        var targets=widgets().Where(peer=>peer!=widget&&!peer.IsDisposed&&peer.Visible&&peer.WindowState!=FormWindowState.Minimized).Select(peer=>new SnapTarget(peer,peer.Bounds)).ToArray();
        var result=engine.Move(new(proposed,widget.Size),targets,widget.DeviceDpi,bypass||!Enabled);
        Show(ref vertical,result.VerticalGuide); Show(ref horizontal,result.HorizontalGuide); return result.Location;
    }
    private static void Show(ref SnapGuideForm? form,Rectangle? bounds)
    { if(bounds==null||bounds.Value.Width<=0||bounds.Value.Height<=0) { form?.Hide(); return; } form??=new(); form.Display(bounds.Value); }
    internal void Clear() { vertical?.Hide(); horizontal?.Hide(); }
    public void Dispose() { vertical?.Dispose(); horizontal?.Dispose(); vertical=null; horizontal=null; }
    private sealed class SnapGuideForm : Form
    {
        internal SnapGuideForm() { FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; AutoScaleMode=AutoScaleMode.None; StartPosition=FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation=>true;
        protected override CreateParams CreateParams { get { var cp=base.CreateParams; cp.ExStyle|=0x80000|0x20|0x80|0x08000000; return cp; } }
        internal void Display(Rectangle bounds)
        {
            if(Visible&&Bounds==bounds)return;
            Location=bounds.Location; ClientSize=bounds.Size;
            _=Handle;
            using var bitmap=new Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using(var graphics=Graphics.FromImage(bitmap))graphics.Clear(Color.FromArgb(200,65,210,245));
            LayeredWidgetBitmap.Update(Handle,Location,bitmap); if(!Visible)Show(); DesktopWidgetNative.KeepOnDesktop(this);
        }
        protected override void WndProc(ref Message m)
        { if(m.Msg==0x84) { m.Result=new IntPtr(-1); return; } if(DesktopWidgetNative.HandleMouseActivation(ref m))return; base.WndProc(ref m); }
    }
}
