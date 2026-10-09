extern alias WallpaperApp;
using App=WallpaperApp::WallpaperControl;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
internal static class WidgetSnappingTests
{
    internal static void Run(Action<bool,string> check)
    {
        var peer=new App.SnapTarget("peer",new(100,100,100,100)); var engine=new App.WidgetSnapEngine();
        foreach(var (raw,expected) in new (int,int)[]{(102,100),(152,150),(127,125),(207,210),(43,40),(198,200),(52,50)})
        { engine.Reset(); var result=engine.Move(new(raw,600,50,40),[peer]); check(result.Location.X==expected&&result.Location.Y==600&&result.VerticalGuide!=null,$"Independent X edge/center/gap snap {raw} → {expected}"); }
        foreach(var (raw,expected) in new (int,int)[]{(102,100),(162,160),(132,130),(207,210),(53,50),(198,200),(62,60)})
        { engine.Reset(); var result=engine.Move(new(600,raw,50,40),[peer]); check(result.Location.Y==expected&&result.Location.X==600&&result.HorizontalGuide!=null,$"Independent Y edge/center/gap snap {raw} → {expected}"); }
        engine.Reset(); check(engine.Move(new(32,600,50,40),[peer]).Location.X==40,"Eight-pixel entry radius includes boundary");
        engine.Reset(); check(engine.Move(new(31,600,50,40),[peer]).Location.X==31,"Outside entry radius preserves raw cursor location");
        engine.Reset(); engine.Move(new(43,600,50,40),[peer]); check(engine.Move(new(51,600,50,40),[peer]).Location.X==40,"Sticky snap survives closer competing edge within release radius");
        check(engine.Move(new(53,600,50,40),[peer]).Location.X==50,"Snap releases beyond twelve pixels and selects nearer edge");
        engine.Reset(); var both=engine.Move(new(102,507,50,40),[peer,new("other",new(700,400,100,100))]);
        check(both.Location==new Point(100,510)&&both.VerticalGuide!=null&&both.HorizontalGuide!=null,"X and Y snap to different target widgets");
        var alt=engine.Move(new(103,508,50,40),[peer],bypass:true); check(alt.Location==new Point(103,508)&&alt.VerticalGuide==null&&alt.HorizontalGuide==null,"Alt preserves raw position and removes guides");
        check(engine.Move(new(31,600,50,40),[peer]).Location.X==31,"Bypass clears hysteresis");
        engine.Reset(); check(engine.Move(new(214,600,50,40),[peer],192).Location.X==220,"High DPI scales spacing to twenty physical pixels");
        engine.Reset(); check(engine.Move(new(14,600,50,40),[peer],192).Location.X==30,"High DPI scales entry radius");
        engine.Reset(); engine.Move(new(102,600,50,40),[peer]); check(engine.Move(new(102,600,50,40),[]).Location.X==102,"Removed target releases sticky snap");
        engine.Reset(); check(engine.Move(new(-793,700,50,40),[new("left monitor",new(-900,-100,100,100))]).Location.X==-790,"Negative monitor coordinates support snapping");
        Exception? error=null; var thread=new Thread(()=> { try { Ui(check); } catch(Exception ex) { error=ex; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(error!=null)throw error;
    }
    private static void Ui(Action<bool,string> check)
    {
        string registry=@"Software\WallpaperControl.SnappingTests-"+Guid.NewGuid().ToString("N");
        try
        {
            var store=new App.AppSettingsStore(registry); check(store.LoadWidgetSnapping(),"Snapping enabled by default"); store.SaveWidgetSnapping(false); check(!store.LoadWidgetSnapping(),"Disabled snapping persists"); store.SaveWidgetSnapping(true); check(store.LoadWidgetSnapping(),"Enabled snapping persists");
            using var target=new Form { ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Bounds=new(800,500,100,100) };
            using var moving=new Form { ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,ClientSize=new(50,40) };
            using var coordinator=new App.WidgetSnapCoordinator(()=>new[]{target,moving},true); var engine=new App.WidgetSnapEngine();
            check(coordinator.Move(moving,new(802,700),engine,false)==new Point(802,700),"Hidden targets are excluded"); target.Show();
            check(coordinator.Move(moving,new(802,700),engine,false).X==800,"Visible target contributes native guide windows");
            var guide=(Form)typeof(App.WidgetSnapCoordinator).GetField("vertical",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(coordinator)!;
            check(guide.Visible&&!guide.ShowInTaskbar,"Native guide visible only in desktop tool window without taskbar entry");
            coordinator.SetEnabled(false); check(coordinator.Move(moving,new(802,700),engine,false)==new Point(802,700),"Global switch disables correction immediately");
            check(!guide.Visible,"Global disable hides existing native guides");
            coordinator.SetEnabled(true); check(coordinator.Move(moving,new(802,700),engine,true)==new Point(802,700),"Alt disables native correction and guides"); target.Hide();
            target.Show();
            using(var dragged=new DragForm { Location=new(50,70),ClientSize=new(50,40),ShowInTaskbar=false })
            {
                Point pointer=new(10,10); var saved=new List<Point>(); int renders=0; bool locked=false;
                App.WidgetSnapping.Register(dragged,coordinator);
                using var handler=new App.WidgetDragHandler(dragged,()=>locked,()=>renders++,saved.Add,()=>pointer);
                dragged.Down(); pointer=new(762,640); dragged.MovePointer();
                check(dragged.Location==new Point(800,700)&&saved.Count==0,"Shared drag applies snap from original screen delta without persisting during motion");
                dragged.Up(); check(saved.SequenceEqual(new[]{new Point(800,700)})&&!guide.Visible,"Release saves snapped final location once and hides guides");
                dragged.Down(); pointer=new(768,640); dragged.MovePointer(); var interrupted=dragged.Location; dragged.Capture=false; pointer=new(820,700); dragged.MovePointer();
                check(dragged.Location==interrupted&&saved.Count==1&&!guide.Visible,"Capture loss aborts shared drag without later motion or persistence");
                locked=true; dragged.Down(); pointer=new(900,800); dragged.MovePointer(); dragged.Up(); check(dragged.Location==interrupted&&saved.Count==1,"Locked widget cannot be dragged or persisted");
            }
            target.Hide();
            App.WidgetSnapping.Register(moving,coordinator); App.WidgetSnapping.Begin(moving); App.WidgetSnapping.End(moving); App.WidgetSnapping.Unregister(moving);
            check(App.WidgetSnapping.Move(moving,new(11,13))==new Point(11,13),"Unregistered drag path preserves original behavior");
            target.Show();
            using(var web=new App.WebWidgetForm(new App.WebWidgetSettings { ReloadOnStartup=false },"de"))
            {
                App.WidgetSnapping.Register(web,coordinator); int settled=0; web.GeometrySettled+=(_,_)=>settled++;
                var method=typeof(App.WebWidgetForm).GetMethod("WndProc",BindingFlags.Instance|BindingFlags.NonPublic)!;
                void Send(int id,IntPtr payload) { object[] values=[Message.Create(web.Handle,id,IntPtr.Zero,payload)]; method.Invoke(web,values); }
                Send(0x231,IntPtr.Zero); web.Capture=true; web.Capture=false;
                var buffer=Marshal.AllocHGlobal(16);
                try
                {
                    Marshal.Copy(new[]{802,700,802+web.Width,700+web.Height},0,buffer,4); Send(0x216,buffer);
                    check(Marshal.ReadInt32(buffer)==800&&settled==0,"Native Web WM_MOVING snaps rectangle despite native capture transitions without persisting");
                }
                finally { Marshal.FreeHGlobal(buffer); }
                Send(0x232,IntPtr.Zero); check(settled==1&&!guide.Visible,"Native Web move completion retains geometry-settled callback and clears guides");
            }
            target.Hide();
            using var dialog=new App.SettingsForm(false,"light",0,0,0,0,0,0,0,0,"",true,false,true,false,true,100,widgetSnappingEnabled:false);
            var checkbox=(CheckBox)typeof(App.SettingsForm).GetField("widgetSnappingCheckBox",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialog)!;
            check(!checkbox.Checked&&checkbox.Tag as string=="SettingsWidgetSnapping","General settings expose global snapping preference");
            check(store.LoadWidgetSnapping(),"Opening settings never persists checkbox draft");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry,false); }
    }
    private sealed class DragForm : Form
    {
        internal void Down()=>OnMouseDown(new(MouseButtons.Left,1,5,5,0));
        internal void MovePointer()=>OnMouseMove(new(MouseButtons.Left,0,5,5,0));
        internal void Up()=>OnMouseUp(new(MouseButtons.Left,1,5,5,0));
    }
}
