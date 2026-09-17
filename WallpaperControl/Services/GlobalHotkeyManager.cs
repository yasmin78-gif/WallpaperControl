using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace WallpaperControl
{
    /// <summary>
    /// Associates a native shortcut registration with its application action and error label.
    /// </summary>
    /// <param name="Id">The action identifier delivered with WM_HOTKEY.</param>
    /// <param name="Modifiers">The native keyboard modifier flags.</param>
    /// <param name="Key">The virtual-key code.</param>
    /// <param name="DisplayName">The localized action name used in conflict messages.</param>
    internal sealed record HotkeyBinding(int Id, uint Modifiers, uint Key, string DisplayName);

    // Called by the window's UI thread. Localization and action dispatch stay in the UI.
    internal sealed class GlobalHotkeyManager
    {
        private readonly Func<IntPtr, int, uint, uint, bool> register;
        private readonly Func<IntPtr, int, bool> unregister;

        /// <summary>
        /// Selects native shortcut registration functions or injected substitutes for testing.
        /// </summary>
        /// <param name="register">An optional native-registration substitute used by tests.</param>
        /// <param name="unregister">An optional native-unregistration substitute used by tests.</param>
        internal GlobalHotkeyManager(
            Func<IntPtr, int, uint, uint, bool>? register = null,
            Func<IntPtr, int, bool>? unregister = null)
        {
            this.register = register ?? RegisterHotKey;
            this.unregister = unregister ?? UnregisterHotKey;
        }

        /// <summary>
        /// Registers enabled shortcut bindings and collects descriptions of registration failures.
        /// </summary>
        /// <param name="handle">The native window handle used by the operation.</param>
        /// <param name="bindings">The shortcut combinations and identifiers to register.</param>
        /// <returns>Descriptions of the shortcut combinations that could not be registered.</returns>
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

        /// <summary>
        /// Releases changed shortcut identifiers before registering their replacement combinations.
        /// </summary>
        /// <param name="handle">The native window handle used by the operation.</param>
        /// <param name="bindings">The shortcut combinations and identifiers to register.</param>
        /// <returns>Descriptions of replacement shortcut combinations that could not be registered.</returns>
        internal IReadOnlyList<string> Replace(IntPtr handle, IEnumerable<HotkeyBinding> bindings)
        {
            var changed = bindings.ToArray();
            // Release all changed combinations first, allowing two actions to swap keys.
            Release(handle, changed.Select(binding => binding.Id));
            return Register(handle, changed);
        }

        /// <summary>
        /// Unregisters the supplied shortcut identifiers for the window.
        /// </summary>
        /// <param name="handle">The native window handle used by the operation.</param>
        /// <param name="ids">The shortcut identifiers to unregister.</param>
        internal void Release(IntPtr handle, IEnumerable<int> ids)
        {
            foreach (int id in ids) unregister(handle, id);
        }
        /// <summary>
        /// Formats a shortcut&apos;s modifiers and virtual key as a user-facing label.
        /// </summary>
        /// <param name="modifiers">The shortcut&apos;s modifier flags.</param>
        /// <param name="key">The virtual-key code for the keyboard shortcut.</param>
        /// <returns>The shortcut label containing modifier names and a key symbol.</returns>
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

        /// <summary>
        /// Registers a system-wide key combination for the specified window and identifier.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="id">The shortcut identifier associated with the registration.</param>
        /// <param name="fsModifiers">The native shortcut modifier flags.</param>
        /// <param name="vk">The native virtual-key code.</param>
        /// <returns>True when registration succeeds; otherwise, false.</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        /// <summary>
        /// Releases a system-wide shortcut registration owned by the specified window.
        /// </summary>
        /// <param name="hWnd">The native window handle used by the operation.</param>
        /// <param name="id">The shortcut identifier associated with the registration.</param>
        /// <returns>True when the registration was removed; otherwise, false.</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id);

    }
}
