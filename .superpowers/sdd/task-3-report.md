# Task 3 Report

Implementation appeared in concurrent commit `54c8c857f40639fdb086c5b3f7ace87bf793d15e` while Task 1 review was running. No trustworthy implementer TDD or verification report was provided. Treat every behavior and test claim as unverified and review the commit directly.

## MVVM fix implementation

Implemented testable cross-platform `LauncherViewModel` and `SettingsViewModel`, observable/command infrastructure, cancellable/debounced Everything search, open-before-save mapping persistence, transactional settings rollback, tray adapter/controller boundary, and explicit primary/secondary launch decisions. WPF launcher/settings actions, results, status, shortcut instruction, Enter, and Escape behavior now use bindings and commands. Settings is one reusable non-modal window; launcher/settings close requests hide, while tray Exit performs application shutdown.

Additional review fixes included:

- `EverythingFolderSearch.SearchAsync` moves the blocking native `wait:true` query to a worker and lets callers cancel without waiting for the native call to return.
- `NotifyIcon` is owned by `NotifyIconAdapter` outside `MainWindow` and driven through `TrayIconController`.
- A secondary `--background` launch exits silently; a manual secondary launch notifies the primary instance.
- Startup registry changes participate in the settings rollback transaction.

## TDD evidence

- Launcher RED: focused test compilation failed with `CS0246` because `LauncherViewModel` did not exist.
- Launcher GREEN: focused run passed 16/16; later expanded launcher coverage is included in the 54-test focused aggregate below.
- Non-blocking Everything RED: two blocking-native tests timed out before the worker refactor.
- Non-blocking Everything GREEN: `EverythingFolderSearchTests` passed 15/15.
- Settings RED: focused test compilation failed with `CS0246` because `SettingsViewModel` did not exist.
- Settings GREEN: focused run passed 13/13.
- Tray/launch-decision RED: focused compilation failed because `ITrayIcon` and `AppLaunchDisposition` did not exist.
- Tray/launch-decision GREEN: focused run passed 6/6.
- Final focused aggregate: Launcher, Everything, Settings, tray, and launch-decision tests passed 54/54.
- Full-solution verification initially exposed a late debounce cancellation overwriting an opener failure. Advancing the search generation before confirmation fixed the race; the focused regression then passed 1/1.

## Final verification

All commands used `DOTNET_CLI_HOME=/tmp/quick-search-dotnet-home` and `/tmp/quick-search-dotnet/dotnet`.

- Format: `dotnet format QuickSearch.sln --verify-no-changes --no-restore --verbosity minimal` exited 0.
- Core Release: `dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj -c Release --no-restore` passed 93/93.
- Windows Release Werror: `dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true` succeeded with 0 warnings and 0 errors.
- Full solution: `dotnet test QuickSearch.sln -c Release --no-restore` passed 93/93.
- `git diff --check` exited 0.

## Runtime limitation

Verification ran on macOS. Cross-platform Core behavior is covered by automated tests and the WPF project cross-compiled successfully, but the Windows-only runtime integrations (WPF focus/lifecycle, global hotkey, tray icon, registry startup, Explorer, and a real Everything 1.4 service/DLL) were not exercised live on Windows.
