using WallpaperControl;

internal static class HotkeyTests
{
    /// <summary>
    /// Runs the hotkey regression checks using the supplied assertion callback.
    /// </summary>
    /// <param name="check">The assertion callback that records a passing check or throws on failure.</param>
    internal static void Run(Action<bool, string> check)
    {
        var active = new Dictionary<int, (uint Modifiers, uint Key)>();
        var calls = new List<string>();
        IntPtr window = new(123);
        bool handlesMatch = true;
        var manager = new GlobalHotkeyManager(
            (handle, id, modifiers, key) =>
            {
                handlesMatch &= handle == window;
                calls.Add("register:" + id);
                if (active.Values.Contains((modifiers, key))) return false;
                active[id] = (modifiers, key);
                return true;
            },
            (handle, id) =>
            {
                handlesMatch &= handle == window;
                calls.Add("release:" + id);
                return active.Remove(id);
            });
        var first = new HotkeyBinding(1, 3, 0x27, "Next");
        var second = new HotkeyBinding(2, 3, 0x50, "Pause");
        check(manager.Register(window, new[] { first, second }).Count == 0 && active.Count == 2 && handlesMatch,
            "Hotkey registration forwards identifiers, keys and window handle");
        calls.Clear();
        check(manager.Register(window, new[] { new HotkeyBinding(3, 0, 0x45, "Disabled"),
            new HotkeyBinding(4, 3, 0, "Disabled") }).Count == 0 && calls.Count == 0,
            "Disabled hotkeys are not registered");
        var failure = manager.Register(window, new[] { new HotkeyBinding(3, 3, 0x27, "Explorer") });
        check(failure.SequenceEqual(new[] { "Explorer (Ctrl+Alt+→)" }) && active.Count == 2,
            "Hotkey conflicts identify action and combination");
        calls.Clear();
        failure = manager.Replace(window, new[] { first with { Key = second.Key }, second with { Key = first.Key } });
        check(failure.Count == 0 && active[1].Key == second.Key && active[2].Key == first.Key &&
            calls.SequenceEqual(new[] { "release:1", "release:2", "register:1", "register:2" }),
            "Swapped hotkeys release both old combinations before registration");
        calls.Clear();
        manager.Replace(window, new[] { first with { Key = 0 } });
        check(!active.ContainsKey(1) && active.ContainsKey(2) && calls.SequenceEqual(new[] { "release:1" }),
            "Disabling one hotkey preserves unchanged registrations");
        calls.Clear();
        manager.Replace(window, Array.Empty<HotkeyBinding>());
        check(calls.Count == 0, "Unchanged settings cause no native calls");
        manager.Release(window, new[] { 1, 2, 3, 4 });
        check(active.Count == 0 && handlesMatch && calls.Count == 4,
            "Hotkey cleanup releases all requested identifiers");
        check(GlobalHotkeyManager.FormatHotkey(15, 0x7B) == "Ctrl+Alt+Shift+Win+F12" &&
            GlobalHotkeyManager.FormatHotkey(2, 0x52) == "Ctrl+R",
            "Hotkey display preserves modifier order and key labels");
    }
}
