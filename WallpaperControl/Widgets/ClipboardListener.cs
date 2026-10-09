using System.Runtime.InteropServices;
namespace WallpaperControl;
internal interface IClipboardSource : IDisposable
{
    event Action<string>? TextChanged;
    bool Enabled { get; set; }
    bool Write(string text);
}
internal sealed class ClipboardListener : NativeWindow,IClipboardSource
{
    public event Action<string>? TextChanged;
    private readonly System.Windows.Forms.Timer retry = new() { Interval = 60 };
    private uint sequence;
    private int attempts;
    private bool disposed;
    private readonly uint excluded = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), history = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    internal bool Registered { get; private set; }
    private bool enabled = true;
    public bool Enabled { get => enabled; set { if(enabled==value)return; enabled=value; retry.Stop(); sequence=GetClipboardSequenceNumber(); } }
    internal ClipboardListener()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3),Caption = "WallpaperControl clipboard listener" });
        sequence = GetClipboardSequenceNumber();
        Registered = AddClipboardFormatListener(Handle);
        retry.Tick += (_,_) => ReadUpdate();
        if(!Registered) { retry.Dispose(); DestroyHandle(); throw new InvalidOperationException("Clipboard listener registration failed."); }
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg == 0x031D && !disposed)
        {
            // Activation never imports the clipboard contents that already existed.
            if(GetClipboardSequenceNumber() != sequence) { attempts=0; ReadUpdate(); }
        }
        base.WndProc(ref m);
    }
    private void ReadUpdate()
    {
        retry.Stop(); if(disposed)return;
        if(!enabled) { sequence=GetClipboardSequenceNumber(); return; }
        if(!OpenClipboard(Handle)) { if(++attempts < 5) retry.Start(); return; }
        string? text = null;
        try
        {
            uint current = GetClipboardSequenceNumber(); if(current==sequence)return; sequence=current;
            if(IsClipboardFormatAvailable(excluded) || !IsClipboardFormatAvailable(13))return;
            if(IsClipboardFormatAvailable(history))
            {
                var data=GetClipboardData(history);
                if(data==IntPtr.Zero || GlobalSize(data).ToUInt64()<4)return;
                var flag=GlobalLock(data); if(flag==IntPtr.Zero)return;
                try { if(Marshal.ReadInt32(flag)==0)return; } finally { GlobalUnlock(data); }
            }
            var handle = GetClipboardData(13); if(handle==IntPtr.Zero)return;
            ulong bytes=GlobalSize(handle).ToUInt64();
            if(bytes < 2 || bytes > (ClipboardHistory.MaximumTextCharacters+1L)*2)return;
            var pointer=GlobalLock(handle); if(pointer==IntPtr.Zero)return;
            try
            {
                var value = Marshal.PtrToStringUni(pointer,(int)(bytes/2)) ?? "";
                int end=value.IndexOf('\0'); text=end>=0 ? value[..end] : null;
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
        if(text != null && !disposed) TextChanged?.Invoke(text);
    }
    public bool Write(string text)
    {
        if(disposed)return false;
        try
        {
            var data = new DataObject(); data.SetData(DataFormats.UnicodeText,text);
            using var flag = new MemoryStream(BitConverter.GetBytes(0));
            data.SetData("CanIncludeInClipboardHistory",flag);
            using var cloudFlag = new MemoryStream(BitConverter.GetBytes(0)); data.SetData("CanUploadToCloudClipboard",cloudFlag);
            using var excludeFlag = new MemoryStream([1]); data.SetData("ExcludeClipboardContentFromMonitorProcessing",excludeFlag);
            Clipboard.SetDataObject(data,true,3,40); sequence = GetClipboardSequenceNumber(); return true;
        }
        catch (ExternalException) { return false; }
    }
    public void Dispose()
    {
        if(disposed)return; disposed=true; retry.Dispose();
        if(Registered) RemoveClipboardFormatListener(Handle); Registered=false; DestroyHandle(); TextChanged=null;
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
}
internal sealed class ClipboardService : IDisposable
{
    private readonly IClipboardSource source;
    internal ClipboardHistory History { get; } = new();
    internal event Action? Changed;
    private bool suspended,disposed;
    internal ClipboardService(IClipboardSource source) { this.source=source; source.TextChanged += Observe; }
    private void Observe(string text) { if(!disposed && !suspended && History.Observe(text,DateTimeOffset.UtcNow)) Changed?.Invoke(); }
    internal void Configure(int capacity) { History.Configure(capacity); Changed?.Invoke(); }
    internal void SetSuspended(bool value) { suspended=value; source.Enabled=!suspended&&!History.Paused; }
    internal void TogglePause() { History.Paused=!History.Paused; source.Enabled=!suspended&&!History.Paused; Changed?.Invoke(); }
    internal bool Copy(Guid id)
    {
        var entry=History.Entries.FirstOrDefault(e=>e.Id==id);
        if(disposed || entry==null || !source.Write(entry.Text))return false;
        return true;
    }
    internal void Remove(Guid id) { History.Remove(id); Changed?.Invoke(); }
    internal void Clear() { source.Enabled=false; History.Clear(); source.Enabled=!suspended&&!History.Paused; Changed?.Invoke(); }
    public void Dispose() { if(disposed)return; disposed=true; source.TextChanged-=Observe; source.Dispose(); History.Clear(); Changed=null; }
}
