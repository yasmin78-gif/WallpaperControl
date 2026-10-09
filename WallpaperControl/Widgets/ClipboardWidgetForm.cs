using System.Drawing.Drawing2D;
namespace WallpaperControl;
internal sealed class ClipboardWidgetForm : Form
{
    private readonly ClipboardService service;
    private readonly WidgetDragHandler drag;
    private readonly CalendarViewport viewport = new(390);
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly ContextMenuStrip menu = new();
    private readonly List<(RectangleF Bounds,Guid Id)> rows = [];
    private ClipboardWidgetSettings settings;
    private string language;
    private bool dark=NotesDialogStyle.ResolveDarkMode(), suspended,powerSuspended,disposed,thumbDragging;
    private float thumbGrab;
    private string? feedbackKey;
    private DateTimeOffset feedbackUntil;
    internal ClipboardWidgetForm(ClipboardService service,ClipboardWidgetSettings settings,string language,Action<Point> moved)
    {
        this.service=service; this.settings=settings.Clone(); this.language=language;
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.None; ClientSize=new(390,120);
        drag=new(this,()=>this.settings.Locked,Render,moved); service.Changed+=Render;
        timer.Tick+=(_,_)=> { if(Visible && (service.History.Entries.Count>0 || feedbackKey!=null)) Render(); }; timer.Start();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp=base.CreateParams; cp.ExStyle|=0x80000|0x80|0x08000000; return cp; } }
    internal void Apply(ClipboardWidgetSettings value,string lang) { settings=value.Clone(); language=lang; Render(); }
    internal void ApplyTheme(bool value) { dark=value; Render(); }
    internal void SetActivitySuspended(bool value) { suspended=value; UpdateSuspension(); }
    internal void SetPowerSuspended(bool value) { powerSuspended=value; UpdateSuspension(); }
    private void UpdateSuspension() { timer.Enabled=!suspended&&!powerSuspended; service.SetSuspended(suspended||powerSuspended); StopDrag(); if(timer.Enabled)Render(); }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Render(); }
    protected override void WndProc(ref Message m)
    {
        if(DesktopWidgetNative.HandleMouseActivation(ref m))return;
        if(m.Msg==0x020A && !suspended&&!powerSuspended) { viewport.Wheel((short)(m.WParam.ToInt64()>>16),SystemInformation.MouseWheelScrollLines); Render(); m.Result=0; return; }
        base.WndProc(ref m);
    }
    private void Render()
    {
        if(disposed||suspended||powerSuspended||!IsHandleCreated)return;
        if(feedbackKey!=null && DateTimeOffset.UtcNow>=feedbackUntil) feedbackKey=null;
        using var bitmap=RenderBitmap(Screen.FromControl(this).WorkingArea.Height,DeviceDpi); ClientSize=bitmap.Size; LayeredWidgetBitmap.Update(Handle,Location,bitmap);
    }
    internal Bitmap RenderBitmap(int workHeight=2000,int dpi=96)
    {
        var entries=service.History.Entries;
        viewport.Update(Math.Max(42,entries.Count*72),settings.MaximumHeight,workHeight,dpi);
        Bitmap bitmap=new((int)Math.Ceiling(390*viewport.Scale),viewport.PhysicalHeight,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(bitmap); g.Clear(Color.Transparent); g.ScaleTransform(viewport.Scale,viewport.Scale); g.SmoothingMode=SmoothingMode.AntiAlias;
        var palette=WidgetDrawing.GetPalette(settings.Style);
        using var path=WidgetDrawing.RoundedRectangle(new(1,1,388,viewport.LogicalHeight-2),12);
        using var fill=new SolidBrush(dark?palette.panel:Color.FromArgb(235,AppTheme.WindowBackground(false)));
        using var text=new SolidBrush(dark?palette.text:AppTheme.TextPrimary(false)); using var muted=new SolidBrush(dark?palette.muted:AppTheme.TextSecondary(false));
        using var accent=new SolidBrush(dark?palette.accent:Color.FromArgb(29,105,184)); using var border=new Pen(accent.Color,settings.Style==SystemWidgetStyle.Glow?2:1);
        using var font=new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel); using var bold=new Font("Segoe UI",13,FontStyle.Bold,GraphicsUnit.Pixel);
        using var icons=new Font("Segoe UI Symbol",16,FontStyle.Regular,GraphicsUnit.Pixel);
        using var format=new StringFormat { Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.LineLimit };
        using var single=new StringFormat { Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap };
        g.FillPath(fill,path); g.DrawPath(border,path); WidgetHeader.Draw(g,Localization.Get("ClipboardTitle",language),language,settings.Style,390,96,text.Color,accent.Color);
        using(var trashPen=new Pen(accent.Color,1.5f))
        {
            g.DrawLine(trashPen,314,18,331,18); g.DrawRectangle(trashPen,318,20,10,13); g.DrawRectangle(trashPen,321,15,4,3);
            g.DrawLine(trashPen,321,23,321,30); g.DrawLine(trashPen,325,23,325,30);
        }
        g.DrawString(service.History.Paused?"▶":"❚❚",icons,accent,352,12);
        var saved=g.Save(); g.SetClip(viewport.ContentBounds); rows.Clear(); float y=54-viewport.ScrollOffset;
        foreach(var entry in entries)
        {
            var bounds=new RectangleF(14,y,viewport.ContentBounds.Width-4,72); rows.Add((bounds,entry.Id));
            if(bounds.IntersectsWith(viewport.ContentBounds))
            {
                g.DrawString("▤",icons,accent,14,y+5);
                g.DrawString(Preview(entry.Text),font,text,new RectangleF(40,y+3,bounds.Width-29,34),format);
                var age=DateTimeOffset.UtcNow-entry.Copied;
                string ageKey=age.TotalHours>=1?"ClipboardAgeHours":age.TotalMinutes>=1?"ClipboardAgeMinutes":"ClipboardAgeSeconds";
                int ageValue=Math.Max(0,(int)(age.TotalHours>=1?age.TotalHours:age.TotalMinutes>=1?age.TotalMinutes:age.TotalSeconds));
                g.DrawString(string.Format(Localization.Get(ageKey,language),ageValue),font,muted,40,y+43);
                using var line=new Pen(Color.FromArgb(65,accent.Color)); g.DrawLine(line,14,y+69,viewport.ContentBounds.Right,y+69);
            }
            y+=72;
        }
        if(entries.Count==0) g.DrawString(Localization.Get("ClipboardEmpty",language),font,muted,14,60);
        g.Restore(saved); WidgetScrollbar.Draw(g,viewport,dark);
        string footer=feedbackKey??(service.History.Paused?"ClipboardPaused":"ClipboardRamOnly");
        g.DrawString(Localization.Get(footer,language),font,muted,new RectangleF(14,viewport.LogicalHeight-23,360,20),single);
        return bitmap;
    }
    internal static string Preview(string value)
    {
        int length=Math.Min(value.Length,220);
        if(length>0&&length<value.Length&&char.IsHighSurrogate(value[length-1]))length--;
        string preview=value[..length].Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
        return preview+(value.Length>length?"…":"");
    }
    internal void Copy(Guid id) { bool success=service.Copy(id); feedbackKey=success?"ClipboardCopied":"ClipboardBusy"; feedbackUntil=DateTimeOffset.UtcNow.AddSeconds(3); Render(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if(suspended||powerSuspended)return;
        var p=new PointF(e.X/viewport.Scale,e.Y/viewport.Scale);
        var row=rows.FirstOrDefault(r=>r.Bounds.Contains(p)&&viewport.ContentBounds.Contains(p));
        if(e.Button==MouseButtons.Right) { ShowMenu(row.Id); return; }
        if(e.Button!=MouseButtons.Left)return;
        if(p.Y<45&&p.X>=345) { service.TogglePause(); return; }
        if(p.Y<45&&p.X>=305) { service.Clear(); viewport.SetOffset(0); Render(); return; }
        var track=viewport.Track; track.Inflate(8,0);
        if(viewport.CanScroll&&track.Contains(p))
        {
            if(p.Y>=viewport.Thumb.Top&&p.Y<=viewport.Thumb.Bottom) { thumbDragging=true; thumbGrab=p.Y-viewport.Thumb.Top; Capture=true; }
            else { viewport.SetOffset(viewport.ScrollOffset+(p.Y<viewport.Thumb.Top?-1:1)*viewport.ViewportHeight); Render(); }
            return;
        }
        if(row.Id!=Guid.Empty) { Copy(row.Id); return; }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if(thumbDragging) { viewport.DragThumb(e.Y/viewport.Scale,thumbGrab); Render(); } else base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if(thumbDragging) { StopDrag(); return; } base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if(!Capture)thumbDragging=false; base.OnMouseCaptureChanged(e); }
    private void StopDrag() { thumbDragging=false; if(Capture)Capture=false; }
    private void ShowMenu(Guid id)
    {
        foreach(ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray())item.Dispose(); menu.Items.Clear();
        string L(string key)=>Localization.Get(key,language);
        if(id!=Guid.Empty)
        {
            menu.Items.Add(L("ClipboardCopy"),null,(_,_)=>Copy(id));
            menu.Items.Add(L("ClipboardDetails"),null,(_,_)=>ShowDetails(id));
            menu.Items.Add(L("ClipboardDelete"),null,(_,_)=>service.Remove(id)); menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(L(service.History.Paused?"ClipboardResume":"ClipboardPause"),null,(_,_)=>service.TogglePause());
        menu.Items.Add(L("ClipboardClear"),null,(_,_)=>service.Clear());
        ContextMenuTheme.Apply(menu,dark); menu.Show(Cursor.Position);
    }
    private void ShowDetails(Guid id)
    {
        var entry=service.History.Entries.FirstOrDefault(e=>e.Id==id); if(entry==null)return;
        using var dialog=new Form { Text=Localization.Get("ClipboardDetails",language),ClientSize=new(700,430),StartPosition=FormStartPosition.CenterScreen,MinimizeBox=false,ShowInTaskbar=false };
        var content=new TextBox { Text=entry.Text,Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill };
        var bottom=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft };
        var close=new Button { Text=Localization.Get("ClipboardClose",language),AutoSize=true,DialogResult=DialogResult.OK };
        var copy=new Button { Text=Localization.Get("ClipboardCopy",language),AutoSize=true }; copy.Click+=(_,_)=>Copy(id);
        bottom.Controls.Add(close); bottom.Controls.Add(copy); dialog.Controls.Add(content); dialog.Controls.Add(bottom); dialog.CancelButton=close;
        NotesDialogStyle.Apply(dialog,settings.Style,dark); dialog.ShowDialog(); content.Clear();
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!disposed) { disposed=true; service.Changed-=Render; timer.Dispose(); drag.Dispose(); menu.Dispose(); rows.Clear(); }
        base.Dispose(disposing);
    }
}
