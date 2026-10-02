namespace WallpaperControl.Video;

// Explicit package capabilities. Never inferred from option errors or native discovery.
internal sealed class MpvOptionProfile
{
    private readonly bool scriptFree;
    internal static readonly MpvOptionProfile Proven = new(false);
    internal static readonly MpvOptionProfile MinimalR1 = new(true);
    private MpvOptionProfile(bool scriptFree) => this.scriptFree = scriptFree;
    internal IReadOnlyDictionary<string, string> Options(nint hwnd)
    {
        var options = new Dictionary<string, string>
            {
                ["wid"] = hwnd.ToString(System.Globalization.CultureInfo.InvariantCulture), ["config"] = "no", ["load-scripts"] = "no", ["terminal"] = "no",
                ["vo"] = "gpu", ["gpu-api"] = "d3d11", ["gpu-context"] = "d3d11", ["hwdec"] = "d3d11va",
                ["keepaspect"] = "yes", ["panscan"] = "1", ["loop-file"] = "inf", ["ao"] = "wasapi", ["audio-exclusive"] = "no", ["audio-device"] = "auto",
                ["input-default-bindings"] = "no", ["input-vo-keyboard"] = "no", ["input-media-keys"] = "no", ["media-controls"] = "no",
                ["osd-level"] = "0", ["osc"] = "no", ["stop-screensaver"] = "no", ["video-latency-hacks"] = "no", ["mute"] = "yes", ["volume"] = "0"
            };
        // These options do not exist when OSC and scripting/plugins are absent
        // from the exact pinned build. Every remaining option is still required.
        if (scriptFree) { options.Remove("osc"); options.Remove("load-scripts"); }
        return options;
    }
    internal void Apply(nint hwnd, Func<string, string, int> set)
    {
        foreach (var pair in Options(hwnd))
        {
            int error = set(pair.Key, pair.Value);
            if (error < 0) throw new InvalidOperationException($"Required mpv option '{pair.Key}' failed: {error}");
        }
    }
}
