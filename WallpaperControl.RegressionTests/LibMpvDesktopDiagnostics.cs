using System.Diagnostics;
using System.Runtime.InteropServices;

// Opt-in user interaction observation. Uses the same checked host/session path.
internal static class LibMpvDesktopDiagnostics
{
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    internal static void Run(string dll,string file,int seconds)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            try
            {
                using var session=new MpvPreparedSession(dll,file,0);
                session.Prepare(()=>Application.DoEvents(),()=>true); session.Present(()=>true); session.Published=true; session.Activate(false,false);
                Console.WriteLine("HIERARCHY "+session.Host.Hierarchy);
                var clock=Stopwatch.StartNew(); int captured=0;
                while(clock.Elapsed.TotalSeconds<seconds)
                {
                    Application.DoEvents(); session.Poll();
                    if(!session.Host.Attached) throw new InvalidOperationException("Interactive desktop attachment lost");
                    if(clock.Elapsed.TotalSeconds>=2+captured*3 && captured<3)
                    {
                        DwmFlush(); var bounds=session.Host.Bounds; using var image=new Bitmap(bounds.Width,bounds.Height);
                        using(var graphics=Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
                        string target=Environment.GetEnvironmentVariable("WALLPAPER_MPV_CAPTURE") ?? Path.Combine(Path.GetTempPath(),"wallpaper-mpv-desktop.png");
                        target=Path.Combine(Path.GetDirectoryName(target)!,Path.GetFileNameWithoutExtension(target)+$"-{++captured}.png"); image.Save(target);
                    }
                    Thread.Sleep(5);
                }
                Console.WriteLine($"INTERACTIVE-END sameSession={session.Id} fileLoaded={session.LoadedEvents} attached={session.Host.Attached}");
                session.Dispose(); var snapshot=NativeHandleSnapshot.Capture();
                Console.WriteLine($"SHUTDOWN live={MpvPreparedSession.Live} handles={snapshot.Total} nvidia={snapshot.NvidiaIpc} names=[{string.Join(';',snapshot.NvidiaNames.Select(x=>x.Key+":"+x.Value))}]");
            }
            catch(Exception ex) { failure=ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
