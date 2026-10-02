extern alias WallpaperApp;
using Video = WallpaperApp::WallpaperControl.Video;
using App = WallpaperApp::WallpaperControl;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class MpvDesktopHost : IDisposable
{
    internal static readonly IReadOnlyDictionary<string,string> Options = new Dictionary<string,string>
    { ["keepaspect"]="yes", ["panscan"]="1", ["input-media-keys"]="no", ["media-controls"]="no", ["stop-screensaver"]="no", ["osc"]="no", ["video-latency-hacks"]="no" };
    private readonly Video.VideoRenderForm form;
    internal readonly App.VideoShellGeneration Shell;
    internal readonly Rectangle Bounds;
    internal nint Handle { get; }
    internal nint Child { get; private set; }
    internal Point ProbeOrigin => new(Bounds.Left + Bounds.Width/2 - 240, Bounds.Top + Bounds.Height/2 - 101);
    internal MpvDesktopHost(nint behind = 0, bool hidden=false)
    {
        if (Screen.AllScreens.Length != 1) throw new InvalidOperationException("One monitor required");
        Shell = Video.VideoNative.Resolve() ?? throw new InvalidOperationException("Shell lease unavailable");
        Bounds = Screen.AllScreens[0].Bounds;
        var previous = Video.VideoNative.SetThreadDpiAwarenessContext(Video.VideoNative.GetWindowDpiAwarenessContext(Shell.Host));
        form = new();
        try
        {
            Handle = form.Handle;
            Video.VideoNative.SetStyle(Handle,-16,(Video.VideoNative.GetStyle(Handle,-16) & ~unchecked((int)0x80000000)) | 0x40000000);
            Video.VideoNative.SetParent(Handle,Shell.Host);
            Video.VideoNative.ConfigureDesktopComposition(Handle,Shell.DefView);
            Place(behind == 0 ? Shell.DefView : behind,show:!hidden);
            if(!hidden)
            {
                form.Show();
                // Show must not move a candidate above its stable predecessor.
                Place(behind == 0 ? Shell.DefView : behind);
            }
        }
        catch { form.Dispose(); throw; }
        finally { if (previous != 0) Video.VideoNative.SetThreadDpiAwarenessContext(previous); }
    }
    internal void Place(nint after,bool show=true)
    {
        var origin = Bounds.Location;
        if (!Video.VideoNative.ScreenToClient(Shell.Host,ref origin) || !Video.VideoNative.SetWindowPos(Handle,after,origin.X,origin.Y,Bounds.Width,Bounds.Height,0x10|0x20|(show?0x40u:0)))
            throw new InvalidOperationException("Own desktop host placement failed");
    }
    internal bool Released => !Video.VideoNative.IsWindow(Handle) && (Child==0 || !Video.VideoNative.IsWindow(Child));
    internal void FindChild()
    {
        Video.VideoNative.EnumChildWindows(Handle,(hwnd,_) =>
        {
            var name = new StringBuilder(256); GetClassName(hwnd,name,name.Capacity);
            if (name.ToString()=="mpv" && Video.VideoNative.GetParent(hwnd)==Handle) Child=hwnd;
            return true;
        },0);
        // mpv's Win32 initialization may enable its embedding parent. Both
        // windows belong to this process; restore the wallpaper input policy.
        EnableWindow(Handle,false);
        if (Child!=0) EnableWindow(Child,false);
    }
    internal Func<bool>? LeaseValidOverride;
    internal bool LeaseValid => LeaseValidOverride?.Invoke() ?? Video.VideoNative.Resolve()==Shell;
    internal bool Attached => LeaseValid && Video.VideoNative.IsWindow(Handle) && Video.VideoNative.GetParent(Handle)==Shell.Host && Child!=0 && Video.VideoNative.IsWindow(Child) && Video.VideoNative.GetParent(Child)==Handle;
    internal bool IsProbeExposed(Point point)
    {
        var hwnd=WindowFromPoint(point); Video.VideoNative.GetWindowThreadProcessId(hwnd,out uint pid);
        return pid==Shell.ProcessId || pid==Environment.ProcessId;
    }
    internal string Hierarchy => $"shell=0x{Shell.Host:X} defView=0x{Shell.DefView:X} host=0x{Handle:X} mpv=0x{Child:X} hostParent=0x{Video.VideoNative.GetParent(Handle):X} mpvParent=0x{Video.VideoNative.GetParent(Child):X}";
    internal (int Hosts,int Children) CountOwned()
    {
        int hosts=0,children=0;
        Video.VideoNative.EnumChildWindows(Shell.Host,(hwnd,_)=>
        {
            Video.VideoNative.GetWindowThreadProcessId(hwnd,out uint pid);
            if(pid==Environment.ProcessId)
            {
                var name=new StringBuilder(256); GetClassName(hwnd,name,name.Capacity);
                if(name.ToString()=="mpv") children++;
                if(Video.VideoNative.GetParent(hwnd)==Shell.Host && name.ToString().StartsWith("WindowsForms",StringComparison.Ordinal)) hosts++;
            }
            return true;
        },0);
        return(hosts,children);
    }
    public void Dispose() => form.Dispose();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint hwnd,bool enabled);
}
