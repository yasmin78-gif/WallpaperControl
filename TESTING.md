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
| Sources | Refresh the first 16 configured distinct sources; additional sources produce the existing partial status and are retained in settings |
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
