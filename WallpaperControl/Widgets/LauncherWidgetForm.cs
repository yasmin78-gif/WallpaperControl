using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace WallpaperControl;
internal sealed class LauncherWidgetForm : Form
{
    private LauncherSettings settings;
    private string language;
    private bool dark=NotesDialogStyle.ResolveDarkMode(),suspended,powerSuspended,disposed,thumbDragging;
    private readonly WidgetDragHandler drag;
    private readonly ToolTip tooltip=new();
    private readonly ContextMenuStrip menu=new();
    private readonly Action<List<LauncherEntry>> saveEntries;
    private readonly Action manage;
    private CalendarViewport viewport;
    private readonly List<(RectangleF Bounds,LauncherEntry? Entry)> cells=[];
    private readonly Dictionary<string,Bitmap> icons=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pending=new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer iconTimer=new() { Interval=100 };
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Target,Bitmap Icon)> completed=new();
    private string? hover;
    private float thumbGrab;
    internal LauncherWidgetForm(LauncherSettings settings,string language,Action<Point> moved,Action<List<LauncherEntry>> saveEntries,Action manage)
    {
        this.settings=settings.Clone(); this.language=language; this.saveEntries=saveEntries; this.manage=manage; viewport=new(LogicalWidth);
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.None; ClientSize=new(LogicalWidth,160); AllowDrop=true;
        drag=new(this,()=>this.settings.Locked,Render,moved);
        iconTimer.Tick+=(_,_)=>DrainIcons();
        DragEnter+=(_,e)=> { LogDrop("Enter",e); e.Effect=DropEffect(e.AllowedEffect,CanDrop(e.Data)); AppLogger.Info($"Launcher drag decision: effect={e.Effect}"); };
        DragOver+=(_,e)=>e.Effect=DropEffect(e.AllowedEffect,CanDrop(e.Data));
        DragLeave+=(_,_)=>AppLogger.Info("Launcher drag Leave");
        DragDrop+=(_,e)=>HandleDrop(e);
    }
    private int CellWidth=>settings.ShowNames?99:76;
    private int LogicalWidth=>Math.Clamp(settings.Columns,2,6)*CellWidth+44;
    private int CellHeight=>settings.ShowNames?78:50;
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams { get { var cp=base.CreateParams; cp.ExStyle|=0x80000|0x80|0x08000000; return cp; } }
    internal void Apply(LauncherSettings value,string lang)
    { bool resize=value.Columns!=settings.Columns||value.ShowNames!=settings.ShowNames; settings=value.Clone(); language=lang; AllowDrop=!suspended&&!powerSuspended; if(resize)viewport=new(LogicalWidth); PruneIcons(); Render(); }
    internal void ApplyTheme(bool value) { dark=value; Render(); }
    internal void SetActivitySuspended(bool value) { suspended=value; UpdateSuspension(); }
    internal void SetPowerSuspended(bool value) { powerSuspended=value; UpdateSuspension(); }
    private void UpdateSuspension() { AllowDrop=!suspended&&!powerSuspended; StopDrag(); tooltip.Hide(this); if(!suspended&&!powerSuspended) { DrainIcons(); Render(); } }
    protected override void OnShown(EventArgs e) { base.OnShown(e); AppLogger.Info($"Launcher drop target ready: allowDrop={AllowDrop} enabled={Enabled} positionLocked={settings.Locked}"); Render(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Render(); }
    protected override void WndProc(ref Message m)
    { if(DesktopWidgetNative.HandleMouseActivation(ref m))return; if(m.Msg==0x020A&&!suspended&&!powerSuspended) { viewport.Wheel((short)(m.WParam.ToInt64()>>16),SystemInformation.MouseWheelScrollLines); Render(); m.Result=0; return; } base.WndProc(ref m); }
    private void Render()
    {
        if(disposed||suspended||powerSuspended||!IsHandleCreated)return;
        RequestIcons(); using var bitmap=RenderBitmap(Screen.FromControl(this).WorkingArea.Height,DeviceDpi); ClientSize=bitmap.Size; LayeredWidgetBitmap.Update(Handle,Location,bitmap);
    }
    internal Bitmap RenderBitmap(int workHeight=2000,int dpi=96)
    {
        int columns=Math.Clamp(settings.Columns,2,6),width=LogicalWidth; int count=settings.Entries.Count+1;
        viewport.Update(((count+columns-1)/columns)*CellHeight,settings.MaximumHeight,workHeight,dpi);
        Bitmap bitmap=new((int)Math.Ceiling(width*viewport.Scale),viewport.PhysicalHeight,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g=Graphics.FromImage(bitmap); g.Clear(Color.Transparent); g.ScaleTransform(viewport.Scale,viewport.Scale); g.SmoothingMode=SmoothingMode.AntiAlias;
        var palette=WidgetDrawing.GetPalette(settings.Style);
        using var path=WidgetDrawing.RoundedRectangle(new(1,1,width-2,viewport.LogicalHeight-2),12);
        using var panel=new SolidBrush(dark?palette.panel:Color.FromArgb(235,AppTheme.WindowBackground(false)));
        using var text=new SolidBrush(dark?palette.text:AppTheme.TextPrimary(false)); using var accent=new SolidBrush(dark?palette.accent:Color.FromArgb(29,105,184));
        using var border=new Pen(accent.Color,settings.Style==SystemWidgetStyle.Glow?2:1); using var font=new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel); using var bold=new Font("Segoe UI",13,FontStyle.Bold,GraphicsUnit.Pixel);
        using var format=new StringFormat { Alignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap };
        g.FillPath(panel,path); g.DrawPath(border,path); WidgetHeader.Draw(g,Localization.Get("LauncherTitle",language),language,settings.Style,width,48,text.Color,accent.Color); g.DrawString("⋮",bold,accent,width-30,14);
        var state=g.Save(); g.SetClip(viewport.ContentBounds); cells.Clear();
        for(int i=0;i<count;i++)
        {
            var entry=i<settings.Entries.Count?settings.Entries[i]:null;
            var bounds=new RectangleF(12+(i%columns)*CellWidth,54+(i/columns)*CellHeight-viewport.ScrollOffset,CellWidth,CellHeight); cells.Add((bounds,entry)); if(!bounds.IntersectsWith(viewport.ContentBounds))continue;
            if(entry!=null&&LauncherTargets.IsWebsite(entry.Target))
            {
                using var globe=new Pen(accent.Color,1.5f); var circle=new RectangleF(bounds.X+(CellWidth-30)/2f,bounds.Y+7,30,30); g.DrawEllipse(globe,circle); g.DrawEllipse(globe,circle.X+8,circle.Y,14,30); g.DrawLine(globe,circle.Left,circle.Y+15,circle.Right,circle.Y+15);
            }
            else if(entry!=null&&icons.TryGetValue(entry.Target,out var icon))g.DrawImage(icon,new RectangleF(bounds.X+(CellWidth-32)/2f,bounds.Y+5,32,32));
            else { using var large=new Font("Segoe UI Symbol",28,FontStyle.Regular,GraphicsUnit.Pixel); g.DrawString(entry==null?"+":"▣",large,accent,bounds.X+(CellWidth-30)/2f,bounds.Y+2); }
            if(settings.ShowNames)g.DrawString(entry?.Name??Localization.Get("LauncherNew",language),font,text,new RectangleF(bounds.X+2,bounds.Y+42,CellWidth-4,24),format);
        }
        g.Restore(state); WidgetScrollbar.Draw(g,viewport,dark); return bitmap;
    }
    private void RequestIcons()
    {
        var entries=settings.Entries.Where(e=>!LauncherTargets.IsWebsite(e.Target)&&!icons.ContainsKey(e.Target)&&pending.Add(e.Target)).ToArray(); if(entries.Length==0)return;
        iconTimer.Start(); _=Task.Run(()=>
        {
            foreach(var entry in entries)
            {
                lock(completed) { if(disposed)break; }
                Bitmap icon; try { icon=LauncherIcons.Load(entry); } catch(Exception ex) when(ex is ArgumentException or IOException or System.ComponentModel.Win32Exception or ExternalException or NotSupportedException or System.Security.SecurityException) { icon=SystemIcons.Application.ToBitmap(); }
                lock(completed) { if(disposed)icon.Dispose(); else completed.Enqueue((entry.Target,icon)); }
            }
        });
    }
    private void DrainIcons()
    {
        bool changed=false;
        while(completed.TryDequeue(out var value)) { pending.Remove(value.Target); if(!disposed&&settings.Entries.Any(e=>string.Equals(e.Target,value.Target,StringComparison.OrdinalIgnoreCase))) { if(icons.Remove(value.Target,out var old))old.Dispose(); icons[value.Target]=value.Icon; changed=true; } else value.Icon.Dispose(); }
        if(pending.Count==0)iconTimer.Stop(); if(changed)Render();
    }
    private void PruneIcons() { foreach(var key in icons.Keys.Where(key=>!settings.Entries.Any(e=>string.Equals(e.Target,key,StringComparison.OrdinalIgnoreCase))).ToArray()) { icons[key].Dispose(); icons.Remove(key); } }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if(suspended||powerSuspended)return; var p=new PointF(e.X/viewport.Scale,e.Y/viewport.Scale);
        if(p.Y<45&&p.X>=LogicalWidth-40) { ShowMenu(null); return; }
        var cell=viewport.ContentBounds.Contains(p)?cells.FirstOrDefault(c=>c.Bounds.Contains(p)):default;
        if(e.Button==MouseButtons.Right) { ShowMenu(cell.Entry); return; }
        if(e.Button!=MouseButtons.Left)return;
        var track=viewport.Track; track.Inflate(7,0);
        if(viewport.CanScroll&&track.Contains(p)) { if(viewport.Thumb.Contains(p)) { thumbDragging=true; thumbGrab=p.Y-viewport.Thumb.Top; Capture=true; } else { viewport.SetOffset(viewport.ScrollOffset+(p.Y<viewport.Thumb.Top?-1:1)*viewport.ViewportHeight); Render(); } return; }
        if(cell.Bounds!=RectangleF.Empty) { if(cell.Entry==null)Add(null); else Start(cell.Entry); return; } base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(thumbDragging) { viewport.DragThumb(e.Y/viewport.Scale,thumbGrab); Render(); return; }
        var p=new PointF(e.X/viewport.Scale,e.Y/viewport.Scale); var entry=viewport.ContentBounds.Contains(p)?cells.FirstOrDefault(c=>c.Bounds.Contains(p)).Entry:null;
        string? next=entry?.Name; if(next!=hover) { hover=next; tooltip.SetToolTip(this,next); } base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { if(thumbDragging) { StopDrag(); return; } base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if(!Capture)thumbDragging=false; base.OnMouseCaptureChanged(e); }
    private void StopDrag() { thumbDragging=false; if(Capture)Capture=false; }
    private void Start(LauncherEntry entry,bool parent=false)
    {
        try { LauncherTargets.Start(entry,parent); }
        catch(Exception ex) when(ex is IOException or ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        { AppLogger.Info($"Launcher start failed: type={ex.GetType().Name}"); MessageBox.Show(Localization.Get("LauncherStartFailed",language),entry.Name,MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }
    private void ShowMenu(LauncherEntry? entry)
    { PopulateMenu(entry); menu.Show(Cursor.Position); }
    private void PopulateMenu(LauncherEntry? entry)
    {
        foreach(ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray())item.Dispose(); menu.Items.Clear(); string L(string key)=>Localization.Get(key,language);
        if(entry!=null)
        {
            menu.Items.Add(L("LauncherOpen"),null,(_,_)=>Start(entry));
            if(LauncherTargets.IsWebsite(entry.Target))menu.Items.Add(L("LauncherCopyAddress"),null,(_,_)=> { try { Clipboard.SetText(entry.Target); } catch(ExternalException) { MessageBox.Show(L("ClipboardBusy")); } });
            else if(LauncherTargets.Kind(entry.Target)!=LauncherTargetKind.Folder)menu.Items.Add(L("LauncherOpenFolder"),null,(_,_)=>Start(entry,true));
            menu.Items.Add(L("LauncherEdit"),null,(_,_)=>Edit(entry));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L("LauncherDelete"),null,(_,_)=>saveEntries(settings.Entries.Where(e=>e.Id!=entry.Id).ToList()));
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(L("LauncherAdd"),null,(_,_)=>Add(null)); menu.Items.Add(L("LauncherManage"),null,(_,_)=>manage()); ContextMenuTheme.Apply(menu,dark);
    }
    private void Edit(LauncherEntry entry)
    { using var dialog=new LauncherEntryForm(entry,language,settings.Style,dark); if(dialog.ShowDialog()!=DialogResult.OK||dialog.Result==null)return; var entries=settings.Entries.Select(e=>e.Id==entry.Id?dialog.Result:e).ToList(); saveEntries(entries); }
    private void Add(string? target)
    { if(settings.Entries.Count>=LauncherSettings.MaximumEntries)return; using var dialog=new LauncherEntryForm(null,language,settings.Style,dark,target); if(dialog.ShowDialog()!=DialogResult.OK||dialog.Result==null)return; var entries=settings.Entries.ToList(); entries.Add(dialog.Result); saveEntries(entries); }
    internal bool CanDrop(IDataObject? data)=>!suspended&&!powerSuspended&&data!=null&&settings.Entries.Count<LauncherSettings.MaximumEntries&&(DropTargets(data).Any()||LauncherShellDrop.Offered(data));
    private void HandleDrop(DragEventArgs e)
    {
        LogDrop("Drop",e); e.Effect=DropEffect(e.AllowedEffect,CanDrop(e.Data)); if(e.Effect==DragDropEffects.None||e.Data==null)return;
        var staging=Path.Combine(Path.GetTempPath(),"WallpaperControl.LauncherDrop-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=DropTargets(e.Data);
            if(paths.Length==0||paths.Any(path=>!LauncherTargets.IsWebsite(path)&&!System.IO.File.Exists(path)&&!Directory.Exists(path)))
                paths=LauncherShellDrop.Materialize(e.Data,staging).ToArray();
            AppLogger.Info($"Launcher drop resolved: targets={paths.Length}");
            foreach(var target in paths) { if(settings.Entries.Count>=LauncherSettings.MaximumEntries)break; Add(target); }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or COMException or NotSupportedException)
        { AppLogger.Info($"Launcher shell drop failed: type={ex.GetType().Name}"); MessageBox.Show(Localization.Get("LauncherShortcutFailed",language)); }
        finally
        {
            // Only delete shortcuts created in this unique staging directory, never source paths.
            if(Directory.Exists(staging))
            {
                try { foreach(var file in Directory.GetFiles(staging,"*.lnk",SearchOption.TopDirectoryOnly))System.IO.File.Delete(file); Directory.Delete(staging,false); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { AppLogger.Info($"Launcher drop staging cleanup failed: type={ex.GetType().Name}"); }
            }
        }
    }
    internal static DragDropEffects DropEffect(DragDropEffects allowed,bool supported)
    {
        if(!supported)return DragDropEffects.None;
        if((allowed&DragDropEffects.Copy)!=0)return DragDropEffects.Copy;
        if((allowed&DragDropEffects.Link)!=0)return DragDropEffects.Link;
        // Never report Move: the launcher leaves the source item in place.
        return DragDropEffects.None;
    }
    private void LogDrop(string stage,DragEventArgs e) => AppLogger.Info($"Launcher drag {stage}: {DropFormatSummary(e.Data)} allowedEffects={e.AllowedEffect} currentEffect={e.Effect} keyState={e.KeyState} allowDrop={AllowDrop} enabled={Enabled} positionLocked={settings.Locked} activitySuspended={suspended} powerSuspended={powerSuspended} entries={settings.Entries.Count}");
    internal static string DropFormatSummary(IDataObject? data)
    {
        if(data==null)return "formats=none";
        try
        {
            var formats=data.GetFormats(false);
            // Only known standard names are safe to log: custom format names may themselves contain private data.
            string[] standard=["FileDrop","UnicodeText","Text","OEMText","Shell IDList Array","FileGroupDescriptor","FileGroupDescriptorW","FileContents","Preferred DropEffect","Performed DropEffect","Logical Performed DropEffect","DragImageBits","DragContext","IsShowingLayered","IsShowingText","DropDescription","UniformResourceLocator","UniformResourceLocatorW","HTML Format","Rich Text Format","Shell Object Offsets","DataObjectAttributes","DataObjectAttributesRequiringElevation"];
            var known=formats.Where(name=>standard.Contains(name,StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
            return $"formats=[{string.Join(",",known)}] otherFormatCount={formats.Count(name=>!standard.Contains(name,StringComparer.Ordinal))}";
        }
        catch(Exception ex) { return $"formats=unavailable errorType={ex.GetType().Name}"; }
    }
    internal static string[] DropTargets(IDataObject? data)
    { if(data?.GetData(DataFormats.FileDrop) is string[] paths)return paths.Take(LauncherSettings.MaximumEntries).ToArray(); if(data?.GetData(DataFormats.UnicodeText) is string text&&LauncherTargets.IsWebsite(text.Trim()))return [text.Trim()]; return []; }
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!disposed) { lock(completed)disposed=true; iconTimer.Dispose(); drag.Dispose(); tooltip.Dispose(); menu.Dispose(); foreach(var icon in icons.Values)icon.Dispose(); icons.Clear(); while(completed.TryDequeue(out var value))value.Icon.Dispose(); cells.Clear(); }
        base.Dispose(disposing);
    }
}
