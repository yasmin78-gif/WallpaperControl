namespace WallpaperControl;
internal sealed class LauncherEntryList : UserControl
{
    private readonly ListBox list=new() { Dock=DockStyle.Fill,DisplayMember="Name",IntegralHeight=false };
    private readonly FlowLayoutPanel buttons=new() { Dock=DockStyle.Bottom,Height=42,WrapContents=false };
    private readonly List<Button> actions=[];
    private Func<List<LauncherEntry>> read;
    private Action<List<LauncherEntry>> save;
    private string language;
    private SystemWidgetStyle style;
    private bool dark;
    internal LauncherEntryList(Func<List<LauncherEntry>> read,Action<List<LauncherEntry>> save,string language,SystemWidgetStyle style,bool dark)
    {
        this.read=read; this.save=save; this.language=language; this.style=style; this.dark=dark;
        Size=new(550,245); Controls.Add(list); Controls.Add(buttons);
        string[] keys=["LauncherAdd","LauncherEdit","LauncherDelete","LauncherUp","LauncherDown"];
        for(int i=0;i<keys.Length;i++) { int action=i; var button=new Button { AutoSize=true,Tag=keys[i] }; button.Click+=(_,_)=>Act(action); actions.Add(button); buttons.Controls.Add(button); }
        list.SelectedIndexChanged+=(_,_)=>UpdateButtons(); list.DoubleClick+=(_,_)=>Act(1); RefreshEntries(); ApplyPresentation(language,style,dark);
    }
    internal void Configure(Func<List<LauncherEntry>> getter,Action<List<LauncherEntry>> setter) { read=getter; save=setter; RefreshEntries(); }
    internal void ApplyPresentation(string lang,SystemWidgetStyle value,bool darkMode)
    { language=lang; style=value; dark=darkMode; list.BackColor=AppTheme.InputBackground(dark); list.ForeColor=AppTheme.TextPrimary(dark); foreach(var button in actions)button.Text=Localization.Get((string)button.Tag!,language); UpdateButtons(); }
    internal void RefreshEntries(int selected=-1)
    { list.BeginUpdate(); try { list.Items.Clear(); foreach(var entry in read())list.Items.Add(entry); list.SelectedIndex=list.Items.Count==0?-1:Math.Clamp(selected,0,list.Items.Count-1); } finally { list.EndUpdate(); } UpdateButtons(); }
    private void UpdateButtons()
    { int index=list.SelectedIndex; actions[0].Enabled=list.Items.Count<LauncherSettings.MaximumEntries; actions[1].Enabled=actions[2].Enabled=index>=0; actions[3].Enabled=index>0; actions[4].Enabled=index>=0&&index<list.Items.Count-1; }
    private void Act(int action)
    {
        var entries=read().ToList(); int index=list.SelectedIndex;
        if(action==0||action==1)
        {
            if(action==1&&(index<0||index>=entries.Count)||action==0&&entries.Count>=LauncherSettings.MaximumEntries)return;
            using var dialog=new LauncherEntryForm(action==0?null:entries[index],language,style,dark);
            if(dialog.ShowDialog(FindForm())!=DialogResult.OK||dialog.Result==null)return;
            if(action==0) { entries.Add(dialog.Result); index=entries.Count-1; } else entries[index]=dialog.Result;
        }
        else if(index>=0&&index<entries.Count) { if(action==2)entries.RemoveAt(index); else { int direction=action==3?-1:1; LauncherTargets.Move(entries,index,direction); index=Math.Clamp(index+direction,0,Math.Max(0,entries.Count-1)); } }
        else return;
        save(entries); RefreshEntries(index);
    }
}
