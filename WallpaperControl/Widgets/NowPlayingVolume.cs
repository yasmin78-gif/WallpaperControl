using System.Runtime.InteropServices;
namespace WallpaperControl;
internal static class NowPlayingVolume
{
    // Core Audio's default multimedia render endpoint; no transport commands are sent.
    internal static bool Adjust(int delta)
    {
        return WithEndpoint(audio =>
        {
            Marshal.ThrowExceptionForHR(audio.GetMasterVolumeLevelScalar(out float value));
            var context = Guid.Empty; Marshal.ThrowExceptionForHR(audio.SetMasterVolumeLevelScalar(Math.Clamp(value + delta * .02f, 0, 1), ref context)); return true;
        });
    }
    internal static bool Read(out float value)
    {
        float result = 0;
        bool success = WithEndpoint(audio => { Marshal.ThrowExceptionForHR(audio.GetMasterVolumeLevelScalar(out result)); return true; });
        value = result; return success;
    }
    private static bool WithEndpoint(Func<IEndpointVolume,bool> action)
    {
        object? enumerator = null, endpoint = null, volume = null;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!);
            Marshal.ThrowExceptionForHR(((IDeviceEnumerator)enumerator!).GetDefaultAudioEndpoint(0, 1, out var device)); endpoint = device;
            var iid = typeof(IEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out volume));
            return action((IEndpointVolume)volume);
        }
        catch (Exception ex) { AppLogger.Info($"Now Playing volume access failed: {ex.Message}"); return false; }
        finally { foreach (var item in new[] { volume, endpoint, enumerator }) if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); }
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }
}
