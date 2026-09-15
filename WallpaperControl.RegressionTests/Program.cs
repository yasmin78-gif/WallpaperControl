using System.Diagnostics;
using System.Collections.Concurrent;
using WallpaperControl;
using System.Text;
using System.IO.Pipes;

try
{
    if (args.Contains("--fail-test")) throw new Exception("Intentional test failure.");

    if (args.Contains("--probe"))
    {
        using var probe = new SingleInstanceGuard(args[1]);
        return probe.IsPrimary ? 10 : 20;
    }

    string instanceName = @"Local\WallpaperControl.Tests." + Guid.NewGuid().ToString("N");
    int passed = 0;
    void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        passed++;
    }
    var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "statistics.json");
    PersistentStatisticsData Data(int views) => new()
    {
        Wallpapers = new() { new() { Path = @"C:\wallpapers\test.jpg", Views = views } }
    };
    Check(StatisticsStorage.Load(path).Wallpapers.Count == 0, "Missing files start empty");
    StatisticsStorage.SaveData(path, Data(1));
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 1, "First save round-trips");
    StatisticsStorage.SaveData(path, Data(2));
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 2 &&
          StatisticsStorage.Load(path + ".bak").Wallpapers.Single().Views == 1, "Replacement retains previous version");
    File.WriteAllText(path, "broken json");
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 1, "Corrupt primary recovers backup");
    Check(Directory.GetFiles(root, "statistics.json.corrupt-*").Any(p => File.ReadAllText(p) == "broken json"), "Corrupt bytes preserved");
    StatisticsStorage.SaveData(path, Data(3));
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 3 &&
          StatisticsStorage.Load(path + ".bak").Wallpapers.Single().Views == 1, "Save after recovery preserves good backup");
    File.WriteAllText(path, "null");
    StatisticsStorage.SaveData(path, Data(4));
    Check(StatisticsStorage.Load(path + ".bak").Wallpapers.Single().Views == 1, "Save never backs up corrupt data");
    File.WriteAllText(path, "{\"Wallpapers\":[null]}");
    File.WriteAllText(path + ".bak", "invalid backup");
    Check(StatisticsStorage.Load(path).Wallpapers.Count == 0 &&
          Directory.GetFiles(root, "*.corrupt-*").Length == 4, "Both invalid files preserved before empty fallback");
    StatisticsStorage.SaveData(path, Data(5));
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        bool failed = false;
        try { StatisticsStorage.SaveData(path, Data(6)); }
        catch (IOException) { failed = true; }
        Check(failed, "Locked primary is not replaced");
    }
    Check(StatisticsStorage.Load(path).Wallpapers.Single().Views == 5 && Directory.GetFiles(root, "*.tmp").Length == 0,
        "Failed save retains original and cleans temporary file");
    int Probe()
    {
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--probe", instanceName }, UseShellExecute = false })!;
        if (!child.WaitForExit(5000)) { child.Kill(); throw new Exception("Probe timed out"); }
        return child.ExitCode;
    }
    using (var primary = new SingleInstanceGuard(instanceName))
    {
        Check(primary.IsPrimary && Probe() == 20, "Second process cannot own instance lock");
    }
    Check(Probe() == 10, "Lock released after primary exits");
    string? Parse(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return RemoteCommandServer.ReadCommandAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
    }
    Check(Parse("next\n") == "next" && Parse("show\r\n") == "show", "LF and CRLF accepted");
    Check(Parse(new string('x', 64) + "\n")?.Length == 64, "Length limit accepted");
    Check(Parse(new string('x', 65) + "\n") == null, "Oversized command rejected");
    Check(Parse("next") == null, "Incomplete command rejected");
    using (var silent = new SilentStream())
    {
        var watch = Stopwatch.StartNew();
        Check(RemoteCommandServer.ReadCommandAsync(silent, CancellationToken.None).GetAwaiter().GetResult() == null &&
            watch.Elapsed < TimeSpan.FromSeconds(5), "Silent input times out");
    }
    using (var silent = new SilentStream())
    using (var stop = new CancellationTokenSource(50))
    {
        bool cancelled = false;
        try { RemoteCommandServer.ReadCommandAsync(silent, stop.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Shutdown cancellation propagates");
    }
    using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    {
        ArgumentList = { "--fail-test" }, UseShellExecute = false, RedirectStandardError = true
    })!)
    {
        if (!child.WaitForExit(5000)) { child.Kill(); throw new Exception("Failure test timed out"); }
        Check(child.ExitCode == 1 && child.StandardError.ReadToEnd().Contains("Intentional test failure"),
            "Test failure returns exit code 1 and console diagnostic");
    }
    if (!args.Contains("--skip-pipe"))
    {
    string pipeName = "WallpaperControl.Tests." + Guid.NewGuid().ToString("N");
    var received = new BlockingCollection<string>();
    using (var server = new RemoteCommandServer(received.Add, pipeName))
    {
        server.Start();
        void ExpectNext(string label)
        {
            bool sent = false;
            for (int i = 0; i < 30 && !sent; i++)
            {
                sent = RemoteCommandServer.TrySend("next", pipeName);
                if (!sent) Thread.Sleep(50);
            }
            Check(sent && received.TryTake(out var command, 3000) && command == "next", label);
        }
        NamedPipeClientStream Connect()
        {
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(5000);
            return client;
        }
        using (var idle = Connect())
        {
            Thread.Sleep(RemoteCommandServer.CommandTimeout + TimeSpan.FromMilliseconds(200));
            ExpectNext("Listener continues after idle timeout");
        }
        using (var oversized = Connect())
        {
            oversized.Write(Encoding.UTF8.GetBytes(new string('x', RemoteCommandServer.MaxCommandLength + 1)));
            oversized.Flush();
            ExpectNext("Listener continues after oversized command");
        }
        using (var incomplete = Connect())
            incomplete.Write(Encoding.UTF8.GetBytes("show"));
        ExpectNext("Incomplete command ignored and listener continues");
        foreach (var command in new[] { "show", "next" })
        {
            bool sent = false;
            for (int i = 0; i < 20 && !sent; i++) { sent = RemoteCommandServer.TrySend(command, pipeName); if (!sent) Thread.Sleep(50); }
            Check(sent && received.TryTake(out var actual, 3000) && actual == command, "Remote command forwarded: " + command);
        }
    }
    }
    Console.WriteLine($"All {passed} checks passed. Test data: {root}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}

internal sealed class SilentStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

namespace WallpaperControl
{
    internal static class AppLogger
    {
        internal static void Warning(string message, Exception ex) => Console.WriteLine("Diagnostic: " + message + " " + ex.Message);
    }
}
