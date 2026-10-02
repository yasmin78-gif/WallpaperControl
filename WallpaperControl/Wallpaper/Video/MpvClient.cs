using System.Globalization;
using System.Runtime.InteropServices;

namespace WallpaperControl.Video;

// All client API calls and borrowed event payloads belong to the runtime worker.
// Native wakeups schedule event draining; no polling timer or UI wait is needed.
internal sealed class MpvClient : IAsyncDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void WakeCallback(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetWake(nint self, WakeCallback? callback, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Text(nint self, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Logs(nint self, [MarshalAs(UnmanagedType.LPUTF8Str)] string level);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetString(nint self, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Wait(nint self, double timeout);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Command(nint self, ulong request, nint args);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetProperty(nint self, ulong request, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativeEvent { internal int Type, Error; internal ulong Userdata; internal nint Data; }
    internal readonly record struct Event(int Type, int Error, string Text);
    private readonly MpvRuntime runtime;
    private readonly object gate = new();
    private readonly WakeCallback wakeCallback;
    private Task? retirement;
    private int drainQueued;
    private ulong nextRequest;
    private readonly Dictionary<ulong, TaskCompletionSource> replies = new();
    private Task propertyTail = Task.CompletedTask;
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<Event> receive;
    private readonly nint hwnd;
    private nint handle;
    private bool stopping;
    internal static int LiveClients;
    internal Task Initialized => initialized.Task;
    internal Task PolicyCompletion { get { lock (gate) return propertyTail; } }
    internal MpvClient(MpvRuntime runtime, nint hwnd, Action<Event> receive)
    {
        this.runtime = runtime; this.hwnd = hwnd; this.receive = receive;
        wakeCallback = _ => ScheduleDrain();
        MpvNativeWorker.Post(Initialize);
    }
    private void Initialize()
    {
        try
        {
            handle = runtime.Export<Create>("mpv_create")();
            if (handle == 0) throw new InvalidOperationException("mpv_create failed");
            Interlocked.Increment(ref LiveClients);
            var option = runtime.Export<Text>("mpv_set_option_string");
            runtime.Profile.Apply(hwnd, (key, value) => option(handle, key, value));
            Check(runtime.Export<Logs>("mpv_request_log_messages")(handle, "v"));
            Check(runtime.Export<Simple>("mpv_initialize")(handle));
            if (Read("mpv-version") != runtime.ExpectedBuild) throw new NotSupportedException("Pinned mpv build mismatch");
            runtime.Export<SetWake>("mpv_set_wakeup_callback")(handle, wakeCallback, 0);
            initialized.TrySetResult();
            ScheduleDrain();
        }
        catch (Exception ex)
        {
            initialized.TrySetException(ex); receive(new(-1, ex.HResult, ex.GetType().Name));
            lock (gate) stopping = true;
            DestroyCore();
        }
    }
    private void ScheduleDrain()
    {
        lock (gate)
        {
            if (stopping || Interlocked.Exchange(ref drainQueued, 1) != 0) return;
            MpvNativeWorker.Post(Drain);
        }
    }
    private void Drain()
    {
        Interlocked.Exchange(ref drainQueued, 0);
        lock (gate) { if (stopping || handle == 0) return; }
        try
        {
            var wait = runtime.Export<Wait>("mpv_wait_event");
            // Bounded work per wake permits commands/retirement of the other
            // client to progress even if verbose native logs fill the queue.
            for (int i = 0; i < 128; i++)
            {
                var item = Marshal.PtrToStructure<NativeEvent>(wait(handle, 0));
                if (item.Type == 0) return;
                if (item.Type is 4 or 5 && replies.Remove(item.Userdata, out var reply))
                {
                    if (item.Error < 0) reply.TrySetException(new InvalidOperationException($"mpv command error={item.Error}"));
                    else reply.TrySetResult();
                    continue;
                }
                string text = item.Type == 2 && item.Data != 0
                    ? string.Join(' ', Enumerable.Range(0, 3).Select(j => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(item.Data, j * IntPtr.Size)))) : "";
                int error = item.Error;
                if (item.Type == 7 && item.Data != 0)
                { text = "endReason=" + Marshal.ReadInt32(item.Data); error = Marshal.ReadInt32(item.Data, 4); }
                receive(new(item.Type, error, text));
            }
            ScheduleDrain();
        }
        catch (Exception ex) { receive(new(-1, ex.HResult, ex.GetType().Name)); }
    }
    private void DestroyCore()
    {
        if (handle == 0) return;
        runtime.Export<SetWake>("mpv_set_wakeup_callback")(handle, null, 0);
        try { SetCore("mute", "yes"); SetCore("pause", "yes"); } catch { /* Destroy releases the audio renderer. */ }
        runtime.Export<Destroy>("mpv_terminate_destroy")(handle);
        handle = 0;
        foreach (var reply in replies.Values) reply.TrySetCanceled();
        replies.Clear();
        GC.KeepAlive(wakeCallback);
        Interlocked.Decrement(ref LiveClients);
    }
    internal Task Queue(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            MpvNativeWorker.Post(() =>
            {
                try { if (handle == 0) throw new ObjectDisposedException(nameof(MpvClient)); action(); done.TrySetResult(); }
                catch (Exception ex) { done.TrySetException(ex); }
            });
        }
        return done.Task;
    }
    internal Task SetAsync(string key, string value) => SetManyAsync((key, value));
    internal Task SetManyAsync(params (string Key, string Value)[] properties)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            return propertyTail = SetOrderedAsync(propertyTail, properties);
        }
    }
    private async Task SetOrderedAsync(Task previous, (string Key, string Value)[] properties)
    {
        await previous.ConfigureAwait(false);
        foreach (var property in properties)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await Queue(() =>
            {
                nint text = Marshal.StringToCoTaskMemUTF8(property.Value), pointer = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Marshal.WriteIntPtr(pointer, text);
                    ulong request = ++nextRequest;
                    Check(runtime.Export<SetProperty>("mpv_set_property_async")(handle, request, property.Key, 1, pointer));
                    replies.Add(request, completed);
                }
                finally { Marshal.FreeHGlobal(pointer); Marshal.FreeCoTaskMem(text); }
            }).ConfigureAwait(false);
            await completed.Task.ConfigureAwait(false);
        }
    }
    private void SetCore(string key, string value) => Check(runtime.Export<Text>("mpv_set_property_string")(handle, key, value));
    internal string? Read(string key)
    {
        nint text = runtime.Export<GetString>("mpv_get_property_string")(handle, key);
        try { return text == 0 ? null : Marshal.PtrToStringUTF8(text); }
        finally { if (text != 0) runtime.Export<Free>("mpv_free")(text); }
    }
    internal async Task CommandAsync(params string[] values)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Queue(() =>
        {
        var strings = values.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        nint args = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
        try
        {
            for (int i = 0; i < strings.Length; i++) Marshal.WriteIntPtr(args, i * IntPtr.Size, strings[i]);
            Marshal.WriteIntPtr(args, strings.Length * IntPtr.Size, 0);
            ulong request = ++nextRequest;
            Check(runtime.Export<Command>("mpv_command_async")(handle, request, args));
            replies.Add(request, completed);
        }
        finally { Marshal.FreeHGlobal(args); foreach (nint text in strings) Marshal.FreeCoTaskMem(text); }
        });
        await completed.Task;
    }
    private static void Check(int error) { if (error < 0) throw new InvalidOperationException($"mpv error={error}"); }
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (retirement == null)
            {
                stopping = true;
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                retirement = done.Task;
                MpvNativeWorker.Post(() =>
                {
                    try { DestroyCore(); done.TrySetResult(); }
                    catch (Exception ex) { done.TrySetException(ex); }
                });
            }
            return new(retirement);
        }
    }
}
