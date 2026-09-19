# Regression and pre-release checks

On Windows with the .NET 10 SDK, run from the repository root:

```powershell
pwsh -File ./Test.ps1
```

This is the required complete regression entry point. It builds and runs the existing executable harness, including child-process IPC tests and the hidden-window stabilization checks. It prints each passing check and an `All N checks passed` summary. The script propagates failures and rejects a successful process that does not report a positive check count. It does not start MainForm or take over the desktop wallpaper.

`dotnet test -c Release` deliberately exits non-zero with directions to this command: this repository does not have VSTest discovery. Do not interpret a successful build as a test run. The original `dotnet run --project WallpaperControl.RegressionTests/WallpaperControl.RegressionTests.csproj -c Release` remains available. Run `pwsh -File ./Test.ps1 -VerifyFailurePropagation` to exercise the existing intentional-failure switch; the expected exit code is 1, not success.

Release gates:

```powershell
dotnet build -c Release
pwsh -File ./Test.ps1
dotnet build -c Release -t:Rebuild -p:AnalysisMode=All -p:AnalysisLevel=latest -p:EnforceCodeStyleInBuild=true
dotnet build WallpaperControl.RegressionTests/WallpaperControl.RegressionTests.csproj -c Release -t:Rebuild -p:AnalysisMode=All -p:AnalysisLevel=latest -p:EnforceCodeStyleInBuild=true
```

The solution remains application-only; the required script explicitly builds/runs the regression project. This avoids changing release-build scope or introducing a test framework/CI service.

## Calendar resource budgets (H4)

The widget displays 3, 5 or 9 occupied days within its existing 90-day lookahead. Its input is a remote feed, so byte, structure, recurrence and output limits apply independently:

| Boundary | Budget / behavior |
| --- | --- |
| Download | 2 MiB per source, enforced while streaming even without Content-Length; 15-second total fetch/parse cancellation deadline |
| Sources | Refresh the first 16 enabled sources; disabled sources consume no download/parse budget. Additional active sources produce the existing partial status and are retained in settings |
| Parser input | 2,048 VEVENTs; 4,096 total components; nesting depth 8; 50,000 unfolded lines; 16 KiB per unfolded property |
| Recurrence | At most four RRULE/EXRULE properties per component, COUNT at most 10,000, BY-list cardinality product at most 4,096; conservative estimated work at most 250,000 per rule / 2,000,000 per feed |
| Evaluation | Uncounted RRULE/EXRULE search ends at the horizon (one extra boundary day accommodates time zones); unmatched increment limit 128; at most 4,096 materialized occurrences per source and 4,096 merged cached events |
| Processing | Cancellation checked during preflight and occurrence enumeration; elapsed budget checked between occurrences (2 seconds). Parsing runs off the UI thread. These are cooperative limits, not forced interruption of a single library call. Input/work caps provide the independent bounds. |
| All-day display | Clip to today/horizon first; expand at most the requested occupied-day count, clamped to 9, per all-day event |
| Rendering | At most 40 displayed rows; one extra provider row signals overflow. Height is capped at 1,800 pixels and the monitor work area. The footer uses a language-neutral ellipsis when content is omitted. |

The limits allow ordinary personal/work/holiday calendars and many simultaneous appointments. Huge historical feeds or dense/pathological rules can exceed the conservative work estimate; the source is then treated as temporarily unavailable, with the existing error/stale/partial status and last good cached events preserved. No permanent source ban is introduced. The next scheduled refresh can recover. Normal COUNT, EXDATE, all-day and timezone processing remain with Ical.Net; DPAPI storage is unchanged. Diagnostics identify only source number and exception type, never private URLs or feed contents.

Ical.Net's unmatched-increment option does not itself bound successful occurrences or historical startup work. The separate preflight/work/output caps are therefore intentional. See the upstream [EvaluationOptions implementation](https://github.com/ical-org/ical.net/blob/v5.2.3/Ical.Net/Evaluation/EvaluationOptions.cs) and [recurrence evaluator](https://github.com/ical-org/ical.net/blob/v5.2.3/Ical.Net/Evaluation/RecurrencePatternEvaluator.cs).

## Targeted manual Windows verification

- On Windows 10 and 11, start with an active native slideshow and test renderer attachment failure / Explorer restart. Confirm native scheduling remains active on startup failure, and later shell-placement failure falls back to a visible native change. Confirm successful animated transitions are unchanged.
- Use valid A / corrupt B / valid C, sequential and shuffled selection; verify only successful displays enter statistics/history. Repair B and confirm it becomes eligible again.
- Open Calendar settings with synthetic multiple normal and holiday URLs. Verify initial concealment, keyboard reveal/edit/hide, screen-reader value, and encrypted save/reload in each language.
- Start a fresh calendar widget, suspend/resume it, apply settings repeatedly, then disable it during a refresh. Exercise oversized feeds and the overflow footer at differing DPI/work-area sizes.
- Disable Weather during its initial request and repeat suspend/resume. Confirm no post-disposal refresh or redraw.
- Exercise manual and automatic update prompts: click View Release, use Enter with the release action selected, cancel and close. Only acceptance opens the URL once.
- Start with each Windows wallpaper position and change positions externally, manually, and during an animation. Span retains the existing primary-surface behavior; H2 multi-monitor redesign is explicitly outside this pass.

The automated tests use fake native-host and HTTP boundaries plus real WinForms edit controls and bitmap rendering. They do not replace real Explorer, COM wallpaper, browser-launch, long-running refresh or multi-monitor/DPI validation.

## Calendar source management

The complete test entry point also runs `CalendarSourceTests`. It uses disposable, uniquely named HKCU test keys, fake HTTP feeds, hidden native dialogs, and the production calendar bitmap renderer. Tests cover migration/recovery, DPAPI, all persisted source fields, independent drafts, source disabling, metadata, per-source text pixels, holiday appearance, five languages, and scaled layouts. No real calendar URLs or application registry values are required.

`CalendarWidgetSourcesProtectedV1` contains a versioned JSON envelope protected in its entirety with current-user Windows DPAPI. GUIDs identify sources; order, enabled state, type, optional custom name, numbered fallback and opaque numeric ARGB color are retained. A blank custom name permits an optional feed name for widget display. Ical.Net's `Calendar.Name` is the component name (`VCALENDAR`); `X-WR-CALNAME` is a generic property. Bounded, unfolded top-level `X-WR-CALNAME`/RFC 7986 `NAME` lines are therefore read separately and removed before event parsing. Missing, invalid or URL-shaped names fall back to localized numbered labels. Metadata never replaces a stored custom name.

On first successful load of legacy settings, all nonempty normal/holiday entries are migrated and the new encrypted envelope is verified and saved. Legacy encrypted values are never overwritten or removed. Unreadable legacy ciphertext prevents an incomplete automatic migration commit. A verified new envelope, including an empty list, takes precedence on later loads. Saves retain the previous envelope in `CalendarWidgetSourcesProtectedV1Recovery`; an unreadable prior value is retained separately as `CalendarWidgetSourcesProtectedV1Unreadable`. Recovery first tries the previous envelope, then the original legacy values. Recovery snapshots can be older than the most recent edit. DPAPI still requires the same Windows account/profile; these copies are not a portable backup format.

Manager/editor changes affect only immutable source records in copied lists. Applying a manager draft updates Settings' draft; only accepting the parent Settings persists it. Reset and Cancel retain this ownership boundary. Addresses are deliberately read-only while concealed; Show enables single-line editing, and Hide removes native text/undo content. Newly opened editors always start concealed. No URL is used as a list label, tooltip or diagnostic identity.

Normal time/title/detail text uses the source's opaque color in Minimal, Clean and Glow. Details show only a nonempty event location when Show location is enabled; source names are never rendered as detail fallback. Day headings, chrome and special holiday bars remain theme controlled. Default colors are light and distinguishable on the existing dark translucent widget surfaces; arbitrary user colors and the underlying wallpaper can still reduce contrast.

Manual feature checklist (automated tests do not substitute for these checks):

- Open Settings → Widgets → Calendar → Manage calendar sources. Check migrated normal and holiday entries and their count.
- Add/edit/remove a source; reveal and hide its URL, reopen the editor, choose a color, and enter a custom name.
- Show two differently colored calendars, including location details, and verify holiday priority/highlighting.
- Disable/re-enable sources and verify disappearance/recovery after refresh.
- Apply manager edits then Cancel Settings; repeat with Save and restart to verify persistence.
- Check dark/light/system appearance, all five languages, keyboard/Enter/Escape behavior, and real 100/150/200-percent DPI with monitor changes.
- Test an unavailable feed and a working feed together; cached events and refresh timers should retain the v1.8.4 behavior.

## Calendar height and scrolling

`CalendarScrollTests` extends the full suite with logical geometry, real bitmap/pixel comparisons in all three styles at 96/144/192 DPI, hidden native wheel and capture/drag tests, refresh/suspension, persistence and translated Settings layout checks. Source-management and ICS tests remain in the same complete test run.

`CalendarWidgetMaximumHeight` is a DWORD in the existing widget registry key. It stores **logical 96-DPI pixels**, range 300–1400, default 700; small calendars still shrink to their content (minimum normal height 120 logical pixels). Missing/wrong-type values use 700; integer values outside the range clamp to the limits. Clone, the existing manual WidgetManager preview copy, Settings read/save/cancel and Reset carry the preference. This codebase has no `WidgetSettings.CopyFrom` method; its actual manual copy path was updated instead.

The outer physical height is the smaller of natural content height and `min(configuredMaximum × DPI/96, monitorWorkAreaHeight, 1800)`. The existing 1800-physical-pixel bitmap safety budget remains in force, even when a very large configured maximum scales beyond it. Fractional natural heights round up; the ceiling rounds down. Runtime work-area clamps do not alter the stored logical preference. Only Calendar now draws its existing 340-wide logical coordinate system with an explicit DPI transform. Header (54 logical pixels), footer (25), row (21), location (13), day heading/gap (32), clipping and scroll track all use this same coordinate system.

Up to the existing 40-row display limit remain available inside a clipped viewport; height alone no longer removes selected days/rows. The ellipsis still indicates rows omitted by that independent resource limit. No complete-content bitmap is allocated. Scroll state is transient, clamps after every layout/data change, and resets to zero when all content fits. The header and status stay fixed. Wheel/scrollbar redraws use the retained display snapshot, with no provider calls, settings writes, DPAPI, source rebuild or window recreation.

Only Calendar handles its wheel and scrollbar gestures. Windows line/page wheel preferences and partial notches are supported. Wheel input is ignored outside the content area, during dragging and suspension. The drawn scrollbar includes a proportional thumb with an outline; thumb dragging and track paging take precedence over the existing widget drag handler. Locking position still permits scrolling. Normal mouse activation/desktop-band handling is unchanged, and there are no added child controls, global hooks or shared-widget input changes. Actual delivery of wheel input to an inactive widget follows Windows' inactive-window scrolling preference.

Additional manual release checks (not claimed by the automated suite):

- Few appointments: compact widget/no scrollbar, including with a large maximum; many appointments: capped widget/visible scrollbar. Compare small and large maximum settings.
- Wheel up/down, multiple notches, top/bottom clamp; drag scrollbar thumb and click its track. Verify wheel delivery while another application has focus.
- Drag the unlocked widget normally; lock it and confirm that only scrolling remains possible.
- Compare two source colors, events with/without location, and holiday rows entering/leaving the viewport in Minimal/Clean/Glow.
- Refresh or disable/re-enable a source while scrolled; shortening content must remove stale offsets and unnecessary scrollbars.
- Change maximum height, Cancel, then Save and restart. Check the original configured maximum returns on a larger work area.
- Physical 100/150/200-percent DPI/monitor/taskbar changes; verify cap, clipping, header/footer, scrollbar and localized Settings labels.
- Fullscreen pause/resume and disposal/reopening; a reopened widget starts at the top.
