using System.Diagnostics;
using System.Runtime.InteropServices;
namespace WallpaperControl.VideoWallpaperTest;
internal sealed class RenderForm:Form {
 public nint NativeHandle;
 public RenderForm(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;Enabled=false;BackColor=Color.Black;StartPosition=FormStartPosition.Manual;}
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams {get{var c=base.CreateParams;c.ExStyle|=0x08000080;return c;}}
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);NativeHandle=Handle;}
 protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new nint(-1);return;}if(m.Msg==0x21){m.Result=new nint(3);return;}base.WndProc(ref m);}
}
internal sealed class ControlForm:Form {
 readonly ComboBox monitor=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=280};
 readonly TextBox file=new(){Width=520};
 readonly TextBox output=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
 readonly System.Windows.Forms.Timer timer=new(){Interval=250};
 readonly string logs; readonly Process process=Process.GetCurrentProcess();
 RenderForm? render; MfPlayer? player; nint parent; bool manualPause,autoPause,playing,ready,closing;
 int generation,repairs,loops; long savedPosition; bool recovering; DateTime initializeDeadline; DateTime? clearSince; DateTime nextRepair=DateTime.MinValue,lastSample=DateTime.UtcNow;
 TimeSpan cpu=TimeSpan.Zero; string? lastFingerprint;
 long lastPulse=Environment.TickCount64;
 internal long LastPulse=>Interlocked.Read(ref lastPulse);
 public ControlForm(){
  Text="Video-Hintergrund testen — Ctrl+Alt+F12 beendet den Test";Width=800;Height=560;KeyPreview=true;
  logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WallpaperControl","VideoLiveTest",DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));Directory.CreateDirectory(logs);
  File.WriteAllText(Path.Combine(logs,"resources.csv"),"utc,cpu_percent_machine,working_set_mb,private_mb,paused,position_100ns,loops\n");
  var panel=new FlowLayoutPanel{Dock=DockStyle.Top,Height=140,AutoScroll=true};Controls.Add(output);Controls.Add(panel);
  foreach(var s in Screen.AllScreens)monitor.Items.Add($"{s.DeviceName} {s.Bounds}");monitor.SelectedIndex=0;
  panel.Controls.AddRange(new Control[]{file,monitor});
  panel.Controls.Add(new Label{Text="Experimenteller Test: stumm, Endlosschleife, ein Monitor. Bei Bildüberdeckung die Diashow pausieren.",AutoSize=true});
  Button Add(string text,Action action){var b=new Button{Text=text,AutoSize=true};b.Click+=(_,_)=>{try{action();}catch(Exception ex){Fail(ex);}};panel.Controls.Add(b);return b;}
  Add("MP4 auswählen",()=>{using var d=new OpenFileDialog{Filter="MP4|*.mp4"};if(d.ShowDialog(this)==DialogResult.OK)file.Text=d.FileName;});
  Add("Video starten",()=>{recovering=false;repairs=0;savedPosition=0;Start();});
  Add("Pause / Fortsetzen",()=>{manualPause=!manualPause;SyncPause();});Add("Diagnose speichern",()=>Snapshot("manual"));Add("Video stoppen",()=>{recovering=false;Stop();});Add("Test beenden",Close);
  Add("Wiederherstellung testen",()=>{render?.Dispose();});
  Shown+=(_,_)=>{Native.ShowWindow(Handle,1);Log("Ctrl+Alt+F12 global exit registered: "+Native.RegisterHotKey(Handle,1,3,0x7B));Snapshot("baseline");};
  KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)Close();};
  FormClosing+=(_,_)=>{closing=true;timer.Stop();Stop();Native.UnregisterHotKey(Handle,1);};
  timer.Tick+=(_,_)=>Tick();timer.Start();
 }
 void Log(string text){var line=$"{DateTime.Now:O} {text}";output.AppendText(line+Environment.NewLine);File.AppendAllText(Path.Combine(logs,"events.log"),line+Environment.NewLine);}
 void Snapshot(string label)=>File.WriteAllText(Path.Combine(logs,$"{DateTime.Now:HHmmssfff}-{label}.json"),Native.Snapshot());
 internal void ConfigureSmoke(string path){file.Text=path;Shown+=(_,_)=>{
  try{Start();}catch(Exception e){Fail(e);Close();return;}
  var probe=new System.Windows.Forms.Timer{Interval=1000};int seconds=0;
  probe.Tick+=(_,_)=>{seconds++;try{if(seconds==10){manualPause=true;SyncPause();}if(seconds==16){manualPause=false;SyncPause();}if(seconds==22){Log("Injected renderer destruction");render?.Dispose();}if(seconds==30){probe.Stop();probe.Dispose();Close();}}catch(Exception e){Fail(e);probe.Stop();probe.Dispose();Close();}};probe.Start();
 };}
 void Fail(Exception ex){recovering=false;Stop();Log("FAILED safely: "+ex.Message);}
 void Stop(){generation++;ready=false;playing=false;try{player?.Dispose();}finally{player=null;if(render!=null){render.Hide();render.Dispose();render=null;}parent=0;}}
 void Start(){
  Stop();initializeDeadline=DateTime.UtcNow.AddSeconds(15);if(!File.Exists(file.Text)||!string.Equals(Path.GetExtension(file.Text),".mp4",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("Select an existing local MP4.");
  Snapshot("before-attach");var def=Native.DefView();if(def==0)throw new InvalidOperationException("No SHELLDLL_DefView found. Refusing blind attachment.");
  var root=Native.GetParent(def);if(Native.Class(root) is not ("Progman" or "WorkerW"))throw new InvalidOperationException("Unexpected icon host.");
  var bounds=Screen.AllScreens[monitor.SelectedIndex].Bounds;
  parent=root;
  var previous=parent!=0?Native.SetThreadDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(parent)):0;
  try{
   render=new RenderForm();var h=render.Handle;
   if(parent!=0){Native.SetStyle(h,-16,(Native.GetStyle(h,-16)&~unchecked((int)0x80000000))|0x40000000);Native.SetParent(h,parent);if(Native.GetParent(h)!=parent)throw new InvalidOperationException("SetParent failed: "+Marshal.GetLastWin32Error());}
   var pt=bounds.Location;if(parent!=0&&!Native.ScreenToClient(parent,ref pt))throw new InvalidOperationException("ScreenToClient failed");
   render.Paint+=(_,_)=>player?.Update();render.Show();var after=def;
   if(!Native.SetWindowPos(h,after,pt.X,pt.Y,bounds.Width,bounds.Height,0x10|0x20|0x40))throw new InvalidOperationException("SetWindowPos failed");
   int g=generation;player=new MfPlayer(h,file.Text,(type,hr)=>{if(closing||!IsHandleCreated)return;try{BeginInvoke((Action)(()=>{if(g!=generation||player==null)return;try{
    Log($"MF event={type} hr=0x{hr:X8}");if(hr<0)Marshal.ThrowExceptionForHR(hr);
    if(type==6){player.Fill(render.ClientSize);ready=true;if(savedPosition>0)player.Seek(savedPosition);SyncPause();}
    if(type==11){loops++;player.Seek(0);if(!manualPause&&!autoPause)player.Play();}
   }catch(Exception ex){Fail(ex);}}));}catch(InvalidOperationException){}});
   Log($"Attached renderer={Native.Hex(h)} parent={Native.Hex(parent)} strategy=icon-shell-child");Snapshot("after-attach");
  }catch{Stop();throw;}finally{if(previous!=0)Native.SetThreadDpiAwarenessContext(previous);}
 }
 void SyncPause(){if(player==null||!ready)return;bool desired=!manualPause&&!autoPause;if(desired==playing)return;if(desired)player.Play();else player.Pause();playing=desired;Log("Playback "+(playing?"active":"paused")+$" position={savedPosition}");}
 void Tick(){Interlocked.Exchange(ref lastPulse,Environment.TickCount64);try{
  bool previousPause=autoPause;var fs=FullscreenActivityDetector.GetFullscreenState();if(fs==true){autoPause=true;clearSince=null;}else if(fs==false&&autoPause){clearSince??=DateTime.UtcNow;if(DateTime.UtcNow-clearSince>=TimeSpan.FromMilliseconds(750))autoPause=false;}else if(fs==null)clearSince=null;
  if(previousPause!=autoPause)Log("Fullscreen automatic pause="+autoPause);
  SyncPause();
  if(player!=null&&!ready&&DateTime.UtcNow>initializeDeadline)throw new TimeoutException("Media initialization exceeded 15 seconds");
  if(render!=null){bool drift=render.IsDisposed||!Native.IsWindow(render.NativeHandle)||(parent!=0&&(!Native.IsWindow(parent)||Native.GetParent(render.NativeHandle)!=parent))||!Native.IsWindowVisible(render.NativeHandle)||Native.IsIconic(render.NativeHandle);
   if(drift){Snapshot("drift");Stop();recovering=true;}
  }
  if(recovering&&DateTime.UtcNow>=nextRepair){if(repairs>=3){recovering=false;Stop();Log("Repair budget exhausted. Use Start / repair to retry.");}else{repairs++;nextRepair=DateTime.UtcNow.AddSeconds(3*repairs);Log($"Bounded repair {repairs}/3 position={savedPosition}");try{Start();recovering=false;}catch(Exception e){Log("Repair pending: "+e.Message);}}}
  if(DateTime.UtcNow-lastSample>=TimeSpan.FromSeconds(1)){
   process.Refresh();var now=DateTime.UtcNow;double usage=(process.TotalProcessorTime-cpu).TotalSeconds/(now-lastSample).TotalSeconds/Environment.ProcessorCount*100;cpu=process.TotalProcessorTime;lastSample=now;
   if(player!=null&&ready){try{savedPosition=player.Position100ns();}catch{}}
   File.AppendAllText(Path.Combine(logs,"resources.csv"),FormattableString.Invariant($"{now:O},{usage:F3},{process.WorkingSet64/1048576.0:F2},{process.PrivateMemorySize64/1048576.0:F2},{!playing},{savedPosition},{loops}\n"));
   var fingerprint=string.Join(",",Native.Top().Where(h=>Native.Class(h) is "Progman" or "WorkerW").Select(Native.Hex));if(fingerprint!=lastFingerprint){Snapshot("shell-change");lastFingerprint=fingerprint;}
  }
 }catch(Exception ex){Fail(ex);}}
 protected override void WndProc(ref Message m){if(m.Msg==0x312&&m.WParam==1){Close();return;}base.WndProc(ref m);}
}


