using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace WallpaperControl;
internal static class LauncherShellDrop
{
    internal static bool Offered(System.Windows.Forms.IDataObject? data)=>data?.GetDataPresent("Shell IDList Array",false)==true;
    internal static List<string> Materialize(System.Windows.Forms.IDataObject data,string directory)
    {
        var result=new List<string>();
        if(data.GetData("Shell IDList Array",false) is not MemoryStream stream)return result;
        if(stream.Length>1024*1024)return result; var bytes=stream.ToArray();
        foreach(var (parent,child) in Parse(bytes))
        {
            var parentPtr=Marshal.AllocCoTaskMem(parent.Length); var childPtr=Marshal.AllocCoTaskMem(child.Length); IntPtr combined=IntPtr.Zero;
            try
            {
                Marshal.Copy(parent,0,parentPtr,parent.Length); Marshal.Copy(child,0,childPtr,child.Length); combined=ILCombine(parentPtr,childPtr); if(combined==IntPtr.Zero)continue;
                var path=new StringBuilder(32768);
                if(SHGetPathFromIDListEx(combined,path,(uint)path.Capacity,0)&& (System.IO.File.Exists(path.ToString())||Directory.Exists(path.ToString()))) { result.Add(path.ToString()); continue; }
                // Virtual AppsFolder entries have no file path. Preserve their shell identity in a .lnk.
                Directory.CreateDirectory(directory); string name="Application";
                if(SHGetNameFromIDList(combined,0,out var display)>=0) { try { name=Marshal.PtrToStringUni(display)??name; } finally { Marshal.FreeCoTaskMem(display); } }
                foreach(char invalid in Path.GetInvalidFileNameChars())name=name.Replace(invalid,'_'); name=name.Trim(); if(name.Length==0)name="Application"; if(name.Length>80)name=name[..80];
                var destination=Path.Combine(directory,name+".lnk"); int suffix=2;
                while(System.IO.File.Exists(destination)) { destination=Path.Combine(directory,name+" ("+suffix+").lnk"); suffix++; }
                var link=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
                try { Marshal.ThrowExceptionForHR(((IShellLink)link).SetIDList(combined)); ((IPersistFile)link).Save(destination,true); result.Add(destination); }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            finally { if(combined!=IntPtr.Zero)ILFree(combined); Marshal.FreeCoTaskMem(parentPtr); Marshal.FreeCoTaskMem(childPtr); }
        }
        return result;
    }
    internal static List<(byte[] Parent,byte[] Child)> Parse(byte[] bytes)
    {
        var result=new List<(byte[],byte[])>(); if(bytes.Length<12||bytes.Length>1024*1024)return result;
        uint count=BitConverter.ToUInt32(bytes,0); if(count==0||count>100||4L*(count+2)>bytes.Length)return result;
        byte[]? Read(int index)
        {
            uint offset=BitConverter.ToUInt32(bytes,4+index*4); if(offset<4L*(count+2)||offset>bytes.Length-2)return null; int end=(int)offset;
            while(end<=bytes.Length-2) { ushort size=BitConverter.ToUInt16(bytes,end); if(size==0)return bytes[(int)offset..(end+2)]; if(size<2||size>bytes.Length-end)return null; end+=size; } return null;
        }
        var parent=Read(0); if(parent==null)return result;
        long total=parent.Length;
        for(int i=1;i<=count;i++) { var child=Read(i); if(child==null)return []; total+=child.Length; if(total>1024*1024)return []; result.Add((parent,child)); } return result;
    }
    [DllImport("shell32.dll")] private static extern IntPtr ILCombine(IntPtr parent,IntPtr child);
    [DllImport("shell32.dll")] private static extern void ILFree(IntPtr item);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SHGetPathFromIDListEx(IntPtr item,StringBuilder path,uint length,uint flags);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr item,uint kind,out IntPtr name);
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IShellLink
    {
        [PreserveSig] int GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,IntPtr findData,uint flags);
        [PreserveSig] int GetIDList(out IntPtr item);
        [PreserveSig] int SetIDList(IntPtr item);
    }
}
