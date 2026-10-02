extern alias WallpaperApp;
using Video = WallpaperApp::WallpaperControl.Video;
internal static class MpvProfileChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); count++; }
        var old = Video.MpvOptionProfile.Proven.Options(123);
        var minimal = Video.MpvOptionProfile.MinimalR1.Options(123);
        Check(!minimal.ContainsKey("osc"), "Minimal profile omits OSC");
        Check(!minimal.ContainsKey("load-scripts"), "Minimal profile omits script loader");
        Check(old.Count == 24 && old["osc"] == "no" && old["load-scripts"] == "no", "Proven profile retains all 24 original options");
        Check(minimal.Count == old.Count - 2 && minimal.All(p => old[p.Key] == p.Value), "Every other option remains identical");
        var sent = new Dictionary<string,string>();
        Video.MpvOptionProfile.MinimalR1.Apply(123, (key,value) => { sent.Add(key,value); return 0; });
        Check(sent.Count == minimal.Count && minimal.All(p => sent[p.Key] == p.Value), "Every required option is sent");
        foreach (int error in new[]{-5,-3})
        {
            bool fatal = false;
            try { Video.MpvOptionProfile.MinimalR1.Apply(123, (key,value) => key == "vo" ? error : 0); }
            catch (InvalidOperationException) { fatal = true; }
            Check(fatal, "Unexpected option error remains fatal: " + error);
        }
        bool oldFatal = false;
        try { Video.MpvOptionProfile.Proven.Apply(123, (key,value) => key == "osc" ? -5 : 0); }
        catch (InvalidOperationException) { oldFatal = true; }
        Check(oldFatal && Video.MpvOptionProfile.Proven.Options(123).ContainsKey("osc"), "No fallback or capability guessing after unknown option");
        Console.WriteLine("PROFILE checks=" + count);
    }
}
