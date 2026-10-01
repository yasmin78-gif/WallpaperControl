using System.Diagnostics;
using System.Runtime.InteropServices;

// Test-only diagnostics. Never closes foreign/vendor handles. Total growth is
// always reported; known injected NVIDIA IPC objects are counted separately.
internal readonly record struct NativeHandleSnapshot(int Total, int NvidiaIpc)
{
    internal IReadOnlyDictionary<string, int> Types { get; init; } = new Dictionary<string, int>();
    internal IReadOnlyDictionary<string, int> NvidiaNames { get; init; } = new Dictionary<string, int>();
    internal IReadOnlyDictionary<string, int> NvidiaUniqueObjects { get; init; } = new Dictionary<string, int>();
    internal bool NvidiaObjectIdentitiesAvailable { get; init; }
    internal int NvidiaRedactedObjectIdentities { get; init; }
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int type, nint buffer, int length, out int needed);
    [DllImport("ntdll.dll")] private static extern int NtQueryObject(nint handle, int type, nint buffer, int length, out int needed);
    internal static NativeHandleSnapshot Capture(int? onlyCountProcessId = null)
    {
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Native handle diagnostics require x64");
        using var process = Process.GetCurrentProcess();
        bool nvidia = onlyCountProcessId == null && process.Modules.Cast<ProcessModule>().Any(module =>
            module.ModuleName.StartsWith("nv", StringComparison.OrdinalIgnoreCase) &&
            module.FileVersionInfo.CompanyName?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) == true);
        int length = 4 * 1024 * 1024, total = 0, vendor = 0, redacted = 0;
        nint buffer = 0, detail = Marshal.AllocHGlobal(16384);
        try
        {
            while (true)
            {
                buffer = Marshal.AllocHGlobal(length);
                int status = NtQuerySystemInformation(64, buffer, length, out int needed);
                if (status == 0) break;
                Marshal.FreeHGlobal(buffer); buffer = 0;
                if (status != unchecked((int)0xC0000004) || length >= 128 * 1024 * 1024)
                    throw new InvalidOperationException($"Native handle query unavailable; status=0x{status:X8}");
                length = Math.Max(length * 2, needed);
            }
            var types = new Dictionary<ushort, string>();
            var typeCounts = new Dictionary<string, int>();
            var names = new Dictionary<string, int>();
            var objects = new Dictionary<string, HashSet<nint>>();
            long count = Marshal.ReadInt64(buffer);
            // SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX, x64: header 16, entry 40.
            for (long i = 0; i < count; i++)
            {
                var entry = buffer + checked((int)(16 + i * 40));
                if (Marshal.ReadInt64(entry, 8) != (onlyCountProcessId ?? Environment.ProcessId)) continue;
                total++;
                if (onlyCountProcessId != null) continue; // No foreign-handle access.
                ushort type = unchecked((ushort)Marshal.ReadInt16(entry, 30));
                nint handle = Marshal.ReadIntPtr(entry, 16);
                if (!types.TryGetValue(type, out var name))
                {
                    if (NtQueryObject(handle, 2, detail, 16384, out _) != 0) continue;
                    name = Text(detail); types[type] = name;
                }
                typeCounts[name] = typeCounts.GetValueOrDefault(name) + 1;
                // Avoid querying file/pipe names, which can perform blocking IO.
                if (!nvidia || name is not ("Mutant" or "Section") || NtQueryObject(handle, 1, detail, 16384, out _) != 0) continue;
                string objectName = Text(detail);
                if (objectName.Contains("{2627E361-24E2-4F14-99ED-A20D0685D8DD}", StringComparison.OrdinalIgnoreCase) ||
                    objectName.Contains("{52813408-3561-4705-820a-2b3b78be92ba}", StringComparison.OrdinalIgnoreCase))
                {
                    vendor++;
                    string key = name + ":" + objectName;
                    names[key] = names.GetValueOrDefault(key) + 1;
                    if (!objects.TryGetValue(key, out var unique)) objects[key] = unique = new();
                    nint identity = Marshal.ReadIntPtr(entry);
                    if (identity == 0) redacted++; else unique.Add(identity);
                }
            }
            return new(total, vendor) { Types = typeCounts, NvidiaNames = names,
                NvidiaUniqueObjects = objects.ToDictionary(pair => pair.Key, pair => pair.Value.Count),
                NvidiaObjectIdentitiesAvailable = vendor > 0 && redacted == 0, NvidiaRedactedObjectIdentities = redacted };
        }
        finally { if (buffer != 0) Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(detail); }
    }
    private static string Text(nint unicodeString)
    {
        nint text = Marshal.ReadIntPtr(unicodeString, 8);
        int length = unchecked((ushort)Marshal.ReadInt16(unicodeString)) / 2;
        return text == 0 || length == 0 ? "" : Marshal.PtrToStringUni(text, length) ?? "";
    }
}
