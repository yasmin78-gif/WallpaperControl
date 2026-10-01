using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace WallpaperControl.VideoWallpaperTest;
internal static class Native {
 [DllImport("user32.dll")] public static extern bool ShowWindow(nint h,int command);
 public delegate bool EnumProc(nint h,nint p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb,nint p);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint h,EnumProc cb,nint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(nint h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern nint GetParent(nint h);
 [DllImport("user32.dll")] public static extern nint GetWindow(nint h,uint cmd);
 [DllImport("user32.dll")] public static extern bool IsWindow(nint h);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
 [DllImport("user32.dll")] public static extern bool IsIconic(nint h);
 [DllImport("user32.dll")] public static extern bool IsZoomed(nint h);
 [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h,out uint pid);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] public static extern int GetStyle(nint h,int i);
 [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)] public static extern int SetStyle(nint h,int i,int v);
 [DllImport("user32.dll",SetLastError=true)] public static extern nint SetParent(nint h,nint p);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h,out Rect r);
 [DllImport("user32.dll")] public static extern bool ScreenToClient(nint h,ref Point p);
 [DllImport("user32.dll")] public static extern nint GetWindowDpiAwarenessContext(nint h);
 [DllImport("user32.dll")] public static extern nint SetThreadDpiAwarenessContext(nint c);
 [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint h,int id,uint mod,uint key);
 [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint h,int id);
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int L,T,R,B; public Rectangle Bounds=>Rectangle.FromLTRB(L,T,R,B);}
 public static string Class(nint h){var s=new StringBuilder(256);GetClassName(h,s,256);return s.ToString();}
 public static List<nint> Top(){var l=new List<nint>();EnumWindows((h,_)=>{l.Add(h);return true;},0);return l;}
 public static List<nint> Children(nint h){var l=new List<nint>();EnumChildWindows(h,(c,_)=>{l.Add(c);return true;},0);return l;}
 public static string Hex(nint h)=>$"0x{h.ToInt64():X}";
 public static string Snapshot(){var tops=Top();var shell=tops.Where(h=>Class(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"||Class(h).StartsWith("WindowsForms")).ToList();var all=shell.SelectMany(h=>new[]{h}.Concat(Children(h))).Distinct();return JsonSerializer.Serialize(new {Utc=DateTime.UtcNow,OS=Environment.OSVersion.ToString(),Monitors=Screen.AllScreens.Select(s=>new{s.DeviceName,s.Bounds}),Windows=all.Select(h=>{GetWindowThreadProcessId(h,out var pid);GetWindowRect(h,out var r);return new{Hwnd=Hex(h),Class=Class(h),Parent=Hex(GetParent(h)),Owner=Hex(GetWindow(h,4)),Previous=Hex(GetWindow(h,3)),Next=Hex(GetWindow(h,2)),Visible=IsWindowVisible(h),Bounds=r.Bounds,Pid=pid,Style=$"0x{GetStyle(h,-16):X8}",ExStyle=$"0x{GetStyle(h,-20):X8}"};})},new JsonSerializerOptions{WriteIndented=true});}
 public static nint DefView()=>Top().Where(h=>Class(h) is "Progman" or "WorkerW").SelectMany(h=>Children(h)).FirstOrDefault(h=>Class(h)=="SHELLDLL_DefView");
 public static bool? Fullscreen(){var h=GetForegroundWindow();if(h==0)return null;GetWindowThreadProcessId(h,out var pid);if(pid==Environment.ProcessId||IsIconic(h)||Class(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return false;if(IsZoomed(h)&&(GetStyle(h,-16)&0x00C00000)!=0)return false;if(!GetWindowRect(h,out var r))return null;var m=Screen.FromHandle(h).Bounds;return r.L<=m.Left+2&&r.T<=m.Top+2&&r.R>=m.Right-2&&r.B>=m.Bottom-2;}
}

