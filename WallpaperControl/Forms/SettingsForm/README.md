# Settings dialog code map

`../SettingsForm.cs` coordinates reset, validation, acceptance, and resource cleanup. The files here are parts of the same `SettingsForm` class, following the organization of `MainForm`. They share one dialog instance and its existing state.

## Where to make changes

| File | Responsibility |
| --- | --- |
| `SettingsForm.State.cs` | Control references, preview state, and public accepted values. |
| `SettingsForm.Initialization.cs` | Construct controls and wire events; collapsible sections identify each page and the final preview subscriptions. |
| `SettingsForm.Navigation.cs` | Sidebar, selected-page highlighting, and expanding/collapsing widget navigation. |
| `SettingsForm.General.cs` | User-initiated update check and its result dialogs. |
| `SettingsForm.Rejection.cs` | Rejection folder browser. |
| `SettingsForm.Hotkeys.cs` | Shortcut selectors, defaults, duplicate validation, and value conversion. |
| `SettingsForm.Appearance.cs` | Theme, opacity, Windows theme notifications, and native title-bar styling. |
| `SettingsForm.Language.cs` | Available languages and localized dialog previews. |
| `SettingsForm.ClockPage.cs` | Clock style selection and synchronization with preview cards. |
| `SettingsForm.NextPage.cs` | Next-wallpaper widget styles: Minimal, Clean, and Glow. |
| `SettingsForm.SystemPage.cs` | System-widget styles, module checkboxes, and refresh intervals. |
| `SettingsForm.WeatherPage.cs` | Weather-widget styles and refresh intervals. |
| `SettingsForm.CalendarPage.cs` | Calendar styles, event counts, and refresh intervals. |
| `SettingsForm.WidgetPreview.cs` | Collect control values into a cloned widget settings object for both live preview and saving. |
| `SettingsForm.ClockPreview.cs` | Custom clock-style cards and clock sample drawing. |
| `SettingsForm.Choices.cs` | Small selector types pairing stable values with display labels. |

General preference controls are built in `Initialization` and accepted by `SaveAndClose` in the main file. Widget options are collected by `ReadWidgetSettings` in `WidgetPreview` for both preview and acceptance.

## Preserve these contracts

- All field and auto-property initializers remain together in `State`, in their original order. Do not rely on initializer ordering between partial files.
- Control construction remains in the constructor so readonly references and event ordering are preserved. Use its named regions to navigate individual page layouts. Shared appearance/language controls are intentionally initialized in their existing order.
- `General` remains the selected page each time the dialog opens.
- Live previews operate on cloned widget settings. The caller (`MainForm.Settings.cs`) cancels or commits the preview after `ShowDialog` returns. Do not persist preview changes directly from page handlers.
- `ReadWidgetSettings` is the single mapping from controls to widget options. Its explicit `applySaveDefaults` argument preserves the empty weather-location difference: previews keep an empty string; saving uses `Karlsruhe`.
- Shared widget style choices are populated by `RefreshWidgetStyleChoices` in `Choices`. Theme normalization is owned by `AppSettingsStore`, and Windows theme/native title-bar operations by `WindowsTheme`.
- The next-wallpaper widget defaults to Minimal to retain its original appearance on existing installations. Its selected style survives localization, preview, save, and cancellation; resetting defaults selects Minimal.
- `SaveAndClose` retains its validation order and only closes with `DialogResult.OK` after successful validation. The caller reads the accepted public values only for that result.
- Keep the preview-language and preview-theme recursion guards when rebuilding selectors.
- Widget preview notifications are connected after their controls exist. The additional `Shown` notification restores widget interaction after modal dialog startup.
- Unsubscribe from Windows theme notifications and dispose owned fonts when the dialog is disposed.

## Verification

Run from the repository root:

```powershell
dotnet build WallpaperControl/WallpaperControl.csproj
dotnet run --project WallpaperControl.RegressionTests
```

The structural split was checked against the original C# syntax tokens for all 159 top-level members, including nested classes. Executable statements, signatures, attributes, and field/property initialization order were preserved. Only organization, comments, and layout regions changed.

The regression suite does not drive this live dialog. Before releasing, test opening on General, page navigation, theme/language/opacity previews, widget previews, defaults, duplicate shortcuts, saving, and cancellation. Confirm that canceling widget changes restores the original desktop widgets.
