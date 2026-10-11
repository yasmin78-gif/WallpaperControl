namespace WallpaperControl;
internal sealed class LauncherEntryForm : Form
{
    private readonly PictureBox websitePreview=new() { SizeMode=PictureBoxSizeMode.Zoom,Location=new(18,165),Size=new(64,64) };
    private readonly CancellationTokenSource iconLifetime=new();
    private readonly LauncherWebsiteIcons iconStore;
    private readonly TextBox target;
    private readonly TextBox name;
    private bool manualName;
    private bool settingAutomaticName;
    private readonly Dictionary<string,string> automaticNames=new(StringComparer.Ordinal);
    private string? customIcon;
    private readonly Dictionary<string,string> automaticIcons=new(StringComparer.Ordinal);
    private CancellationTokenSource? previewRequest;
    private readonly List<string> draftIcons=[];
    private bool iconsDisposed;
    private bool iconPreviewClosed;
    internal LauncherEntry? Result { get; private set; }
    internal LauncherEntryForm(LauncherEntry? entry,string language,SystemWidgetStyle style,bool dark,string? dropped=null,IEnumerable<LauncherEntry>? cachedEntries=null,LauncherWebsiteIcons? websiteIcons=null)
    {
        iconStore=websiteIcons??new(LauncherWebsiteIcons.DefaultDirectory);
        customIcon=entry?.CustomIcon;
        manualName=entry!=null;
        foreach(var cached in (cachedEntries??[]).Concat(entry==null?[]:[entry]))
            if(cached.FaviconFor!=null&&cached.Favicon!=null&&WidgetManager.HasValidLauncherFavicon(cached))automaticIcons[cached.FaviconFor]=cached.Favicon;
        string L(string key)=>Localization.Get(key,language);
        Text=L(entry==null?"LauncherAdd":"LauncherEdit"); ClientSize=new(590,285); FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false; StartPosition=FormStartPosition.CenterScreen;
        name=new TextBox { Location=new(18,42),Width=550,Text=entry?.Name??"" };
        name.TextChanged+=(_,_)=> { if(!settingAutomaticName)manualName=true; };
        target=new TextBox { Location=new(18,102),Width=430,Text=entry?.Target??dropped??"" };
        var arguments=new TextBox { Location=new(18,162),Width=550,Text=entry?.Arguments??"" };
        var argumentsLabel=new Label { Text=L("LauncherArguments"),Location=new(18,138),AutoSize=true };
        Controls.AddRange([new Label { Text=L("LauncherName"),Location=new(18,18),AutoSize=true },name,
            new Label { Text=L("LauncherTarget"),Location=new(18,78),AutoSize=true },target,
            argumentsLabel,arguments]);
        var browse=new Button { Text=L("LauncherBrowse"),Location=new(458,100),Width=110 };
        var menu=new ContextMenuStrip(); menu.Items.Add(L("LauncherFile"),null,(_,_)=> { using var picker=new OpenFileDialog { Filter=L("LauncherAllFiles")+"|*.*",CheckFileExists=true }; if(picker.ShowDialog(this)==DialogResult.OK)target.Text=picker.FileName; });
        menu.Items.Add(L("LauncherFolder"),null,(_,_)=> { using var picker=new FolderBrowserDialog(); if(picker.ShowDialog(this)==DialogResult.OK)target.Text=picker.SelectedPath; });
        ContextMenuTheme.Apply(menu,dark); browse.Click+=(_,_)=>menu.Show(browse,new Point(0,browse.Height)); Controls.Add(browse);
        var save=new Button { Text=L("LauncherSave"),Location=new(335,235),Width=110 }; var cancel=new Button { Text=L("LauncherCancel"),Location=new(458,235),Width=110,DialogResult=DialogResult.Cancel };
        var choose=new Button { Text=L("LauncherChooseIcon"),Location=new(100,165),Size=new(260,32) };
        var reset=new Button { Text=L("LauncherWebsiteIcon"),Location=new(100,207),Size=new(260,32) };
        var iconLabel=new Label { Text=L("LauncherCurrentIcon"),Location=new(18,138),AutoSize=true };
        Controls.AddRange([websitePreview,choose,reset,iconLabel]);
        void WebsiteControls()
        {
            bool website=LauncherTargets.IsWebsite(target.Text.Trim());
            websitePreview.Visible=choose.Visible=reset.Visible=iconLabel.Visible=website;
            arguments.Visible=argumentsLabel.Visible=!website;
            ClientSize=new(590,website?330:285); save.Top=cancel.Top=website?280:235; RefreshIconPreview();
        }
        websitePreview.Paint+=(_,e)=> { if(websitePreview.Image==null) { using var pen=new Pen(ForeColor,1.5f); e.Graphics.DrawEllipse(pen,16,16,32,32); e.Graphics.DrawEllipse(pen,25,16,14,32); e.Graphics.DrawLine(pen,16,32,48,32); } };
        target.TextChanged+=(_,_)=>WebsiteControls();
        choose.Click+=async (_,_)=>
        {
            using var picker=new OpenFileDialog { Filter=L("LauncherImageFilter"),CheckFileExists=true };
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            choose.Enabled=reset.Enabled=save.Enabled=false;
            try
            {
                await SelectCustomIconAsync(picker.FileName);
            }
            catch(OperationCanceledException) { }
            catch(Exception) { if(!IsDisposed&&!iconPreviewClosed)MessageBox.Show(this,L("LauncherInvalidIcon"),Text,MessageBoxButtons.OK,MessageBoxIcon.Warning); }
            finally { if(!IsDisposed&&!iconPreviewClosed)choose.Enabled=reset.Enabled=save.Enabled=true; }
        };
        reset.Click+=(_,_)=> { customIcon=null; RefreshIconPreview(); };
        save.Click+=(_,_)=> { try { var created=LauncherTargets.Create(name.Text,target.Text,LauncherTargets.IsWebsite(target.Text.Trim())?"":arguments.Text,entry?.Id);
                Result=created with { CustomIcon=LauncherTargets.IsWebsite(created.Target)?customIcon:null,Favicon=automaticIcons.GetValueOrDefault(created.Target)??entry?.Favicon,FaviconFor=automaticIcons.ContainsKey(created.Target)?created.Target:entry?.FaviconFor }; DialogResult=DialogResult.OK; }
            catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
            { MessageBox.Show(this,L(ex is ArgumentException&&ex.Message=="LauncherArgumentsOnly"?"LauncherArgumentsOnly":"LauncherInvalidTarget"),Text,MessageBoxButtons.OK,MessageBoxIcon.Warning); } };
        Controls.AddRange([save,cancel]); AcceptButton=save; CancelButton=cancel; NotesDialogStyle.Apply(this,style,dark);
        WebsiteControls();
        Shown+=(_,_)=>RefreshIconPreview();
        FormClosed+=(_,_)=> { iconPreviewClosed=true; if(!iconsDisposed)iconLifetime.Cancel(); menu.Dispose(); };
    }
    internal async Task SelectCustomIconAsync(string source)
    {
        var file=await iconStore.ImportAsync(source,iconLifetime.Token);
        if(iconsDisposed||iconPreviewClosed) { iconStore.DeleteUnused([file],Result==null?[]:[Result]); return; }
        draftIcons.Add(file); customIcon=file; RefreshIconPreview();
    }
    private void DrawIconPreview()
    {
        if(iconsDisposed||iconPreviewClosed)return;
        string url=target.Text.Trim();
        var file=LauncherTargets.IsWebsite(url)?customIcon??automaticIcons.GetValueOrDefault(url):null;
        var old=websitePreview.Image; websitePreview.Image=LauncherWebsiteIcons.Read(file); old?.Dispose(); websitePreview.Invalidate();
    }
    private void RefreshIconPreview()
    {
        previewRequest?.Cancel(); previewRequest=null;
        DrawIconPreview();
        string url=target.Text.Trim();
        if(iconsDisposed||iconPreviewClosed)return;
        if(!LauncherTargets.IsWebsite(url)) { SetAutomaticName(""); return; }
        SetAutomaticName(automaticNames.GetValueOrDefault(url)??LauncherWebsiteNames.Host(new Uri(url)));
        bool needName=!manualName&&!automaticNames.ContainsKey(url);
        bool needIcon=customIcon==null&&websitePreview.Image==null;
        if(!IsHandleCreated||(!needName&&!needIcon))return;
        var request=CancellationTokenSource.CreateLinkedTokenSource(iconLifetime.Token); previewRequest=request;
        _=LoadIconPreviewAsync(url,request,needName,needIcon);
    }
    private void SetAutomaticName(string value)
    {
        if(manualName||iconsDisposed||iconPreviewClosed)return;
        settingAutomaticName=true;
        try { name.Text=value; } finally { settingAutomaticName=false; }
    }
    private async Task LoadIconPreviewAsync(string url,CancellationTokenSource request,bool needName,bool needIcon)
    {
        try
        {
            await Task.Delay(400,request.Token);
            Action<string>? discoveredName=needName?value=>
            {
                if(iconsDisposed||iconPreviewClosed||request.IsCancellationRequested||target.Text.Trim()!=url)return;
                automaticNames[url]=value; SetAutomaticName(value);
            }:null;
            var file=await iconStore.FetchAsync(url,request.Token,discoveredName,downloadIcon:needIcon);
            if(file==null)return;
            if(iconsDisposed||iconPreviewClosed||request.IsCancellationRequested||target.Text.Trim()!=url)
            { iconStore.DeleteUnused([file],Result==null?[]:[Result]); return; }
            draftIcons.Add(file); automaticIcons[url]=file;
            // The user may choose a manual image while this request is running.
            DrawIconPreview();
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { if(!iconsDisposed)AppLogger.Info($"Launcher icon preview failed: type={ex.GetType().Name}"); }
        finally { if(ReferenceEquals(previewRequest,request))previewRequest=null; request.Dispose(); }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!iconsDisposed) { iconsDisposed=true; iconLifetime.Cancel(); iconStore.DeleteUnused(draftIcons,Result==null?[]:[Result]); websitePreview.Image?.Dispose(); websitePreview.Image=null; iconStore.Dispose(); iconLifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
