using System.Runtime.InteropServices;
namespace WallpaperControl.VideoWallpaperTest;
[ComVisible(true),Guid("766C8FFB-5FDB-4FEA-A28D-B912996F51BD"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMfCallback {[PreserveSig] void OnMediaPlayerEvent(nint header);}
[ComVisible(true),ClassInterface(ClassInterfaceType.None)]
public sealed class MfCallback:IMfCallback {
 public Action<int,int>? Event;
 public void OnMediaPlayerEvent(nint h){try{Event?.Invoke(Marshal.ReadInt32(h),Marshal.ReadInt32(h,4));}catch{/* Exceptions must not escape COM. */}}
}
// Slots and signatures transcribed from Microsoft's mfplay.h, x64 only.
internal sealed class MfPlayer:IDisposable {
 nint player; readonly MfCallback callback;
 [DllImport("mfplay.dll",CharSet=CharSet.Unicode)] static extern int MFPCreateMediaPlayer(string? url,[MarshalAs(UnmanagedType.Bool)] bool start,uint options,IMfCallback cb,nint hwnd,out nint player);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Simple(nint p);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int BoolArg(nint p,int b);
 [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)] delegate int Create(nint p,[MarshalAs(UnmanagedType.LPWStr)] string url,int sync,nuint data,out nint item);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PtrArg(nint p,nint item);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SizeArg(nint p,out Size native,out Size aspect);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int RectArg(nint p,ref NormalRect rect);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Position(nint p,ref Guid type,ref Variant value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Count(nint p,out uint count);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Attribute(nint p,uint index,ref Guid key,out Variant value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SelectStream(nint p,uint index,int selected);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QuerySelection(nint p,uint index,out int selected);
 [DllImport("ole32.dll")] static extern int PropVariantClear(ref Variant value);
 [StructLayout(LayoutKind.Sequential)] struct NormalRect {public float L,T,R,B;}
 [StructLayout(LayoutKind.Explicit,Size=24)] struct Variant {[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public long Value;}
 T Slot<T>(int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(player),slot*IntPtr.Size));
 static void Check(int hr)=>Marshal.ThrowExceptionForHR(hr);
 public MfPlayer(nint hwnd,string file,Action<int,int> events){
  if(!File.Exists(file))throw new FileNotFoundException("MP4 not found",file);
  callback=new MfCallback{Event=events};
  Check(MFPCreateMediaPlayer(null,false,0,callback,hwnd,out player));
  try{Check(Slot<BoolArg>(24)(player,1));Check(Slot<Create>(14)(player,file,1,0,out var item));try{DisableAudio(item);Check(Slot<PtrArg>(16)(player,item));}finally{if(item!=0)Marshal.Release(item);}}
  catch{Dispose();throw;}
 }
 static T ItemSlot<T>(nint item,int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(item),slot*IntPtr.Size));
 static void DisableAudio(nint item){
  Check(ItemSlot<Count>(item,14)(item,out uint count));
  var key=new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");var audio=new Guid("73647561-0000-0010-8000-00aa00389b71");
  for(uint i=0;i<count;i++){Check(ItemSlot<Attribute>(item,17)(item,i,ref key,out var value));try{
   if(value.Type!=72)throw new InvalidOperationException("Expected VT_CLSID for stream major type");
   if(Marshal.PtrToStructure<Guid>(new nint(value.Value))==audio){Check(ItemSlot<SelectStream>(item,16)(item,i,0));Check(ItemSlot<QuerySelection>(item,15)(item,i,out int selected));if(selected!=0)throw new InvalidOperationException("Audio stream could not be disabled");}
  }finally{PropVariantClear(ref value);}}
 }
 public void Play()=>Check(Slot<Simple>(3)(player));
 public void Pause()=>Check(Slot<Simple>(4)(player));
 public long Position100ns(){var g=Guid.Empty;var v=new Variant();Check(Slot<Position>(8)(player,ref g,ref v));if(v.Type!=20)throw new InvalidOperationException("Unexpected position PROPVARIANT type");return v.Value;}
 public void Seek(long t){var g=Guid.Empty;var v=new Variant{Type=20,Value=t};Check(Slot<Position>(7)(player,ref g,ref v));}
 public void Fill(Size target){Check(Slot<SizeArg>(25)(player,out var n,out var a));if(n.Width<=0||n.Height<=0)throw new InvalidOperationException("No video stream");double src=a.Width>0&&a.Height>0?(double)a.Width/a.Height:(double)n.Width/n.Height;double dst=(double)target.Width/target.Height;var r=new NormalRect{R=1,B=1};if(src>dst){float w=(float)(dst/src);r.L=(1-w)/2;r.R=1-r.L;}else{float h=(float)(src/dst);r.T=(1-h)/2;r.B=1-r.T;}Check(Slot<RectArg>(27)(player,ref r));Check(Slot<BoolArg>(29)(player,0));}
 public void Update(){if(player!=0)Slot<Simple>(32)(player);}
 public void Dispose(){if(player==0)return;callback.Event=null;Slot<Simple>(38)(player);Marshal.Release(player);player=0;GC.KeepAlive(callback);}
}

