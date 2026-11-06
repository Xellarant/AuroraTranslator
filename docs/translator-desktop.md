# Translator desktop: first implementation pass

Implemented November 5, 2026. The Windows desktop host uses WPF and Blazor
Hybrid. It calls the same correction-aware importer as the CLI, with no shell
command construction or console-output parsing. It is a local application and
does not start an HTTP server or contact a package/content feed.

The desktop uses the Aurora Suite's shared Reflections Dark / Aurora.Web palette:
charcoal-violet surfaces, blue primary actions, lavender accents, and the same
success/warning/error colors. CSS theme tokens and the native dark window keep
forms, menus, progress and diagnostics consistent with that palette.

## Launch

From the repository root on Windows:

```powershell
dotnet run --project AuroraTranslator.Desktop/AuroraTranslator.Desktop.csproj
```

Building requires the .NET 10 SDK and Windows desktop targeting support. Running
the framework-dependent build requires the .NET 10 Desktop Runtime and Microsoft
Edge WebView2 Runtime. The host follows Microsoft's
[WPF Blazor guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/tutorials/wpf?view=aspnetcore-10.0).
The first pass does not include an installer or self-contained distribution.

The CLI and non-Windows library/test projects can still be built individually;
the solution now includes a Windows desktop project.

## Workflow

- **Import:** select one content root and a destination database, choose strict
  or best-effort behavior, and start an import or refresh. Native pickers and
  editable paths are available. A destination is always explicit; there is no
  automatic import on startup or implicit installed-content selection.
- **Diagnostics:** inspect an existing database, optionally comparing its original
  source folder. Structural/prepared-projection integrity is shown separately
  from content review/recovery notices. Results include metadata, grouped health
  findings and examples, skipped/retained content, and correction states needing
  attention. Search by message, path or ID, or filter by area. Examples may overlap
  group rows; displayed row totals are not unique element counts.
- **Recent runs:** view the last 30 operations, reuse their parameters, and export
  a JSON report. Reports carry the captured parameters and final result, including
  structured findings and the import diagnostic log. The live log shows its most
  recent 200 messages; successful import reports retain the complete diagnostic log.

Named presets overwrite a preset with the same name, ignoring case. They retain
content/database paths, import policy and source-comparison choice. The last
selection persists across restarts; diagnostic search/area filters persist while
the app stays open. Navigating away from a page does not cancel a run.

Settings, history and the WebView profile are stored under
`%LOCALAPPDATA%\AuroraTranslator`. For isolated testing, set
`AURORA_TRANSLATOR_STATE` to another directory before launching. Settings are
written by temporary-file replacement. Unreadable or unsupported settings are
preserved; a visible warning explains that the session cannot save over them.
A run left unfinished by an unexpected shutdown is labelled **Interrupted** on
restart, with its actual database state left for inspection rather than guessed.

## Operation and policy boundaries

`AuroraTranslator.Application` owns presets, snapshots, persistence and the run
lifetime. `AuroraTranslator.Desktop` owns native dialogs and UI rendering.
`WorkspaceSession` dispatches the synchronous-heavy import work to a background
thread. Only one operation runs at a time, and callbacks are bound to a run ID so
late progress cannot overwrite another result. Settings edited during a run are
for subsequent operations; the running job retains its captured parameters.

Cancellation is cooperative. Closing during a run offers cancellation and keeps
the window open until the job ends. A late cancellation cannot relabel a successful
activation as cancelled. Import failures before activation preserve the working
database through the existing library workflow. One desktop instance is allowed
per settings directory; this does not coordinate unrelated CLI/app processes.
Avoid concurrent writers targeting the same database.

Import policy is unchanged. Strict mode is the initial default. Best effort keeps
usable content with review diagnostics; protected correction failures still block
activation. Source availability/preferences are not importer filters. This UI
does not approve or edit corrections. Previously accepted redundant corrections
can still retire after a successful import under the existing library policy.

The new public `ContentIntegrity.Check(databasePath, optionalSourceRoot)` returns
a `ContentIntegrityReport` with `Passed` and `Failures`. It wraps the existing
read-only checker; missing/invalid databases and failures to finish inspection are
reported without creating or repairing a database. The CLI's
`check-data-integrity` command now uses this same entry point. Content summaries
continue to use the existing `ContentDatabaseReader` APIs, including their
documented rollback-journal recovery behavior; the integrity checker itself does
not recover journals. Inspection cancellation is checked around the synchronous
integrity scan and may take time to finish.

This additive API is **unreleased**. The immutable 0.11.1 packages are unchanged;
desktop development uses project references to this checkout. A subsequent
library release must use a new version. Schema/data **1/19** and preparation
contract **2** are unchanged.

## Verification

- Debug solution build passed. Existing NU1900 vulnerability-feed warnings remain;
  the initial library rebuild also had the existing two CS8632 annotations.
- Six new `desktop workflow` cases passed: settings/restart and malformed-file
  preservation, navigation-independent operation lifetime, overlap refusal and
  late callbacks, cancellation versus successful activation, real strict/best-effort
  import and inspection, failed/cancelled refresh preserving the database, public
  integrity failures, and JSON report round trips.
- Seven existing `source-file comparison` regression cases passed. The full
  character/evaluator suite was not rerun for this UI pass.
- Native-window rehearsal used an isolated settings folder and disposable XML/SQLite
  under `artifacts/desktop-ui-verification/`. It verified best-effort import,
  integrity success with content warnings still visible, filtered result navigation,
  native JSON export, preset saving, and paths/policy/presets/history after restart.
  No installed XML, production database, Lights source, or package artifact was changed.

The tests caught and fixed an unsupported-settings exception before completion.
Pre-commit review also hardened nested settings/history validation: missing paths
or malformed saved findings preserve the original settings file and show the
recovery warning instead of breaking the results page.
The Windows SDK registry lookup and disposable candidate activation required
execution outside the filesystem sandbox. Evidence and the UI-exported report
are under `artifacts/desktop-ui-verification/` (ignored local artifacts).

Later work can add catalog browsing, correction review/editing, additional CLI
launch modes, preset management beyond save/replace, and installer/distribution
support. This first pass provides the routine import/refresh and diagnostic loop.
