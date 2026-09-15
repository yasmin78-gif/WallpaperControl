using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace WallpaperControl
{
    internal sealed record HotkeyBinding(int Id, uint Modifiers, uint Key, string DisplayName);

    // Called by the window's UI thread. Localization and action dispatch stay in the UI.
    internal sealed class GlobalHotkeyManager
    {
        private readonly Func<IntPtr, int, uint, uint, bool> register;
        private readonly Func<IntPtr, int, bool> unregister;

        internal GlobalHotkeyManager(
            Func<IntPtr, int, uint, uint, bool>? register = null,
            Func<IntPtr, int, bool>? unregister = null)
        {
            this.register = register ?? RegisterHotKey;
            this.unregister = unregister ?? UnregisterHotKey;
        }

        internal IReadOnlyList<string> Register(IntPtr handle, IEnumerable<HotkeyBinding> bindings)
        {
            var failures = new List<string>();
            foreach (var binding in bindings)
            {
                if (binding.Modifiers == 0 || binding.Key == 0) continue;
                if (!register(handle, binding.Id, binding.Modifiers, binding.Key))
                    failures.Add($"{binding.DisplayName} ({FormatHotkey(binding.Modifiers, binding.Key)})");
            }
            return failures;
        }

        internal IReadOnlyList<string> Replace(IntPtr handle, IEnumerable<HotkeyBinding> bindings)
        {
            var changed = bindings.ToArray();
            // Release all changed combinations first, allowing two actions to swap keys.
            Release(handle, changed.Select(binding => binding.Id));
            return Register(handle, changed);
        }

        internal void Release(IntPtr handle, IEnumerable<int> ids)
        {
            foreach (int id in ids) unregister(handle, id);
        }
        internal static string FormatHotkey(
            uint modifiers,
            uint key)
        {
            List<string> parts =
                new();

            if ((modifiers & 0x0002) != 0)
                parts.Add("Ctrl");

            if ((modifiers & 0x0001) != 0)
                parts.Add("Alt");

            if ((modifiers & 0x0004) != 0)
                parts.Add("Shift");

            if ((modifiers & 0x0008) != 0)
                parts.Add("Win");

            string keyText =
                key switch
                {
                    0x25 => "←",
                    0x26 => "↑",
                    0x27 => "→",
                    0x28 => "↓",
                    >= 0x70 and <= 0x7B =>
                        "F" + (key - 0x6F),
                    _ => ((char)key).ToString()
                };

            parts.Add(
                keyText);

            return string.Join(
                "+",
                parts);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id);

    }
}
