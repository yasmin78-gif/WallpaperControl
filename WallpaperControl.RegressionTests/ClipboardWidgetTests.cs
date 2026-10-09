extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
internal static class ClipboardWidgetTests
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static void Run(Action<bool,string> check)
    {
        Exception? error=null;
        var thread=new Thread(()=> { try { Ui(check); } catch(Exception ex) { error=ex; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error!=null)throw error;
    }
    private static void Ui(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;
        var history=new App.ClipboardHistory();
        check(history.Entries.Count==0&&!new App.ClipboardWidgetSettings().Enabled,"Clipboard widget starts disabled and history empty");
        foreach(int capacity in new[]{5,10,20,50})
        {
            history.Clear(); history.Configure(capacity); for(int i=0;i<60;i++)history.Observe("test "+i,now.AddSeconds(i));
            check(history.Entries.Count==capacity&&history.Entries[0].Text=="test 59"&&history.Entries[^1].Text=="test "+(60-capacity),$"History bounded to most recent {capacity} entries");
        }
        history.Clear(); history.Configure(5); history.Observe("first",now); history.Observe("second",now); var id=history.Entries[1].Id;
        history.Observe("first",now.AddSeconds(2)); check(history.Entries.Count==2&&history.Entries[0].Id==id&&history.Entries[0].Copied==now.AddSeconds(2),"Duplicate text moves same entry to front with new timestamp");
        history.Observe(" FIRST ",now); check(history.Entries.Count==3,"Case and whitespace remain exact text rather than destructive normalization");
        check(!history.Observe(" ",now)&&!history.Observe(null,now)&&!history.Observe(new string('x',App.ClipboardHistory.MaximumTextCharacters+1),now),"Empty and oversized text are excluded");
        history.Paused=true; check(!history.Observe("sensitive while paused",now)&&history.Entries.Count==3,"Paused recording never retains incoming text"); history.Paused=false;
        history.Remove(id); check(!history.Entries.Any(e=>e.Id==id),"Individual entry deletes"); history.Clear(); check(history.Entries.Count==0,"Clearing history removes entries");
        using var source=new FakeSource(); using var service=new App.ClipboardService(source);
        source.Emit("private-test-marker"); source.Emit("second"); var original=service.History.Entries.Last();
        var beforeCopy=service.History.Entries.ToArray();
        check(service.Copy(original.Id)&&source.Written==original.Text&&service.History.Entries.SequenceEqual(beforeCopy),"Click copies full text without changing entry order or timestamps or auto-pasting");
        check(service.Copy(original.Id)&&service.History.Entries.SequenceEqual(beforeCopy),"Repeated widget copies preserve history order and timestamps");
        source.CanWrite=false; var before=service.History.Entries.ToArray(); check(!service.Copy(before[^1].Id)&&service.History.Entries.SequenceEqual(before),"Busy clipboard preserves history ordering and text"); source.CanWrite=true;
        service.TogglePause(); source.Emit("not recorded"); check(!source.Enabled&&service.History.Entries.Count==2,"Manual pause disables native capture");
        service.SetSuspended(true); service.TogglePause(); check(!source.Enabled,"Manual resume cannot override independent fullscreen/power suspension");
        service.SetSuspended(false); check(source.Enabled&&service.History.Entries.Count==2,"Resume creates no clipboard backlog");
        service.Clear(); check(service.History.Entries.Count==0&&source.Written==original.Text,"Clear history does not empty the actual Windows clipboard");
        string path=@"Software\WallpaperControl.ClipboardTests\"+Guid.NewGuid().ToString("N");
        try
        {
            var settings=new App.WidgetSettings { Clipboard=new() { Locked=true,Capacity=20,MaximumHeight=450,Style=App.SystemWidgetStyle.Glow,Location=new(22,33) } };
            settings.Save(path); var loaded=App.WidgetSettings.Load(path).Clipboard;
            check(loaded.Capacity==20&&loaded.MaximumHeight==450&&loaded.Locked&&loaded.Location==new Point(22,33),"Only clipboard widget preferences persist");
            check(!JsonSerializer.Serialize(settings).Contains("private-test-marker")&&!JsonSerializer.Serialize(settings.Clipboard).Contains("Entries"),"Settings serialization contains no clipboard history or text");
            var clone=settings.Clone(); clone.Clipboard.Capacity=50; check(settings.Clipboard.Capacity==20,"Cloned clipboard preferences do not alias");
            check(App.ClipboardWidgetSettings.Parse("{broken").Capacity==10&&App.ClipboardWidgetSettings.Parse("{\"Capacity\":11,\"MaximumHeight\":99999}").MaximumHeight==1400,"Corrupt preferences and unsupported limits normalize safely");
            using var editor=new App.WidgetSettingsEditor(settings);
            check(editor.WidgetKeys.Contains("ClipboardTitle")&&editor.ReadWidgetSettings(false).Clipboard.Capacity==20,"Clipboard settings page exposes configured capacity");
            foreach(string language in new[]{"de","en","fr","es","ja"}) { editor.ApplyPresentation(true,language); check(editor.ReadWidgetSettings(false).Clipboard.Capacity==20,$"Localization preserves clipboard preferences in {language}"); }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path,false); }
        service.Configure(50); for(int i=0;i<50;i++)source.Emit("Example "+i+"\nA second line with URL https://example.com and code.");
        using var widget=new App.ClipboardWidgetForm(service,new() { Capacity=50,MaximumHeight=300 },"de",_=>{});
        foreach(var style in Enum.GetValues<App.SystemWidgetStyle>()) foreach(int dpi in new[]{96,144,192}) foreach(bool dark in new[]{true,false})
        {
            widget.Apply(new() { Capacity=50,MaximumHeight=300,Style=style },"de"); widget.ApplyTheme(dark); using var bitmap=widget.RenderBitmap(2000,dpi);
            check(bitmap.Width==390*dpi/96&&bitmap.Height==300*dpi/96,$"Clipboard scroll viewport: {style}, dpi={dpi}, dark={dark}");
        }
        var viewport=(App.CalendarViewport)typeof(App.ClipboardWidgetForm).GetField("viewport",Fields)!.GetValue(widget)!;
        check(viewport.CanScroll&&viewport.DragThumb(viewport.Track.Bottom,0)&&viewport.ScrollOffset>0,"Scrollbar thumb can drag through long history");
        viewport.SetOffset(0); check(viewport.Wheel(-120,3)&&viewport.ScrollOffset>0,"Mouse wheel scrolls clipboard history");
        check(App.ClipboardWidgetForm.Preview(new string('x',300)).EndsWith('…')&&!App.ClipboardWidgetForm.Preview("a\r\nb\tc").Contains('\n'),"Preview is bounded and flattens lines without altering source text");
        string unicode=new string('x',219)+"😀"+"tail"; check(!App.ClipboardWidgetForm.Preview(unicode).Contains('\uD83D'),"Preview truncation never leaves dangling surrogate");
        string? output=Environment.GetEnvironmentVariable("WALLPAPER_CLIPBOARD_PREVIEW"); if(output!=null) { widget.Apply(new(){Style=App.SystemWidgetStyle.Glow,MaximumHeight=400},"de"); widget.ApplyTheme(true); using var bitmap=widget.RenderBitmap(); bitmap.Save(output); }
        widget.Dispose(); service.Dispose(); check(source.Disposed&&source.SubscriberCount==0&&service.History.Entries.Count==0,"Disabling/closing disposes source, subscriptions and RAM history");
        using var listener=new App.ClipboardListener(); check(listener.Registered&&listener.Handle!=IntPtr.Zero,"Native clipboard listener registers invisible message-only HWND");
        int observed=0; listener.TextChanged+=_=>observed++; Application.DoEvents(); check(observed==0,"Listener does not import preexisting Windows clipboard on startup");
        listener.Enabled=false; listener.Enabled=true; Application.DoEvents(); check(observed==0,"Resuming native listener does not import paused clipboard contents");
        listener.Dispose(); check(!listener.Registered&&listener.Handle==IntPtr.Zero,"Native listener unregisters and destroys HWND");
    }
    private sealed class FakeSource : App.IClipboardSource
    {
        public event Action<string>? TextChanged;
        public bool Enabled { get; set; }=true;
        internal bool CanWrite=true,Disposed;
        internal string? Written;
        internal int SubscriberCount=>TextChanged?.GetInvocationList().Length??0;
        internal void Emit(string text) { if(Enabled)TextChanged?.Invoke(text); }
        public bool Write(string text) { if(!CanWrite)return false; Written=text; return true; }
        public void Dispose() { Disposed=true; TextChanged=null; }
    }
}
