namespace WallpaperControl;
internal sealed class LauncherEntryForm : Form
{
    internal LauncherEntry? Result { get; private set; }
    internal LauncherEntryForm(LauncherEntry? entry,string language,SystemWidgetStyle style,bool dark,string? dropped=null)
    {
        string L(string key)=>Localization.Get(key,language);
        Text=L(entry==null?"LauncherAdd":"LauncherEdit"); ClientSize=new(590,285); FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false; StartPosition=FormStartPosition.CenterScreen;
        var name=new TextBox { Location=new(18,42),Width=550,Text=entry?.Name??"" };
        var target=new TextBox { Location=new(18,102),Width=430,Text=entry?.Target??dropped??"" };
        var arguments=new TextBox { Location=new(18,162),Width=550,Text=entry?.Arguments??"" };
        Controls.AddRange([new Label { Text=L("LauncherName"),Location=new(18,18),AutoSize=true },name,
            new Label { Text=L("LauncherTarget"),Location=new(18,78),AutoSize=true },target,
            new Label { Text=L("LauncherArguments"),Location=new(18,138),AutoSize=true },arguments]);
        var browse=new Button { Text=L("LauncherBrowse"),Location=new(458,100),Width=110 };
        var menu=new ContextMenuStrip(); menu.Items.Add(L("LauncherFile"),null,(_,_)=> { using var picker=new OpenFileDialog { Filter=L("LauncherAllFiles")+"|*.*",CheckFileExists=true }; if(picker.ShowDialog(this)==DialogResult.OK)target.Text=picker.FileName; });
        menu.Items.Add(L("LauncherFolder"),null,(_,_)=> { using var picker=new FolderBrowserDialog(); if(picker.ShowDialog(this)==DialogResult.OK)target.Text=picker.SelectedPath; });
        ContextMenuTheme.Apply(menu,dark); browse.Click+=(_,_)=>menu.Show(browse,new Point(0,browse.Height)); Controls.Add(browse);
        var save=new Button { Text=L("LauncherSave"),Location=new(335,235),Width=110 }; var cancel=new Button { Text=L("LauncherCancel"),Location=new(458,235),Width=110,DialogResult=DialogResult.Cancel };
        save.Click+=(_,_)=> { try { Result=LauncherTargets.Create(name.Text,target.Text,arguments.Text,entry?.Id); DialogResult=DialogResult.OK; }
            catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
            { MessageBox.Show(this,L(ex is ArgumentException&&ex.Message=="LauncherArgumentsOnly"?"LauncherArgumentsOnly":"LauncherInvalidTarget"),Text,MessageBoxButtons.OK,MessageBoxIcon.Warning); } };
        Controls.AddRange([save,cancel]); AcceptButton=save; CancelButton=cancel; NotesDialogStyle.Apply(this,style,dark);
        FormClosed+=(_,_)=>menu.Dispose();
    }
}
