# QuickSearch Fluent UI and Online Update Design

**Date:** 2026-08-12

## Goal

Polish QuickSearch into a coherent Windows 11-style desktop experience and add a safe, user-controlled online update path backed by the existing public GitHub Releases and Inno Setup packaging workflow.

The work must preserve the hierarchical rule explorer, clipboard shortcut opening, settings transaction semantics, `%LOCALAPPDATA%\QuickSearch\config.json`, and the stable Inno Setup `AppId`.

## Decisions

- Add `WPF-UI` 4.3.x to the Windows project for Fluent symbol icons and selected modern controls. Keep the current view models and application architecture.
- Do not migrate packaging to Velopack in this iteration. Existing Inno Setup installations must remain directly upgradeable by the next installer.
- Replace text glyphs and `Microsoft.VisualBasic.Interaction.InputBox` with typed Fluent icons and an in-application folder editor dialog.
- Check for updates after startup when enabled, but never block launcher initialization or clipboard activation.
- Require explicit user confirmation before downloading and applying an update. Do not silently install in the background.
- Use the public GitHub Releases API without embedding a token. Cache automatic checks so one installation checks at most once per 24 hours; manual checks always run immediately.
- Publish and verify a SHA-256 sidecar for every QuickSearch installer. Never execute a downloaded installer without a matching valid checksum.

## Visual Direction

QuickSearch is a compact navigation utility, so the visual language should feel like a disciplined File Explorer companion rather than a dashboard.

### Tokens

- Canvas: `#F6F7F9`
- Surface: `#FFFFFF`
- Primary text: `#1F2328`
- Secondary text: `#667085`
- Accent: `#2563EB`
- Accent wash: `#EAF1FF`
- Border: `#E2E6EC`
- Error: `#C42B1C`
- Success: `#0F7B6C`

Use Segoe UI Variable with restrained 8/12-pixel radii, one-pixel borders, and no decorative drop shadows. The signature element is the horizontal pinned-folder rail: compact Fluent folder icons, readable names, and a clear selected state.

### Main Window

- Replace `⌕`, `▰`, `⚙`, arrows, and other font-dependent characters with `Wpf.Ui.Controls.SymbolIcon` values.
- Keep the three-region information architecture: pinned rail, category tree, current category/search results.
- Use a real settings icon button with tooltip and automation name.
- Use consistent 36-pixel compact controls in navigation areas and 40-pixel controls for primary actions.
- Keep visible keyboard focus and `Ctrl+K` search focus.

### Folder Editing

New-child and rename actions open one reusable modal dialog owned by `MainWindow`:

- context-specific title and explanation;
- folder-name text box with focus and selection;
- inline validation for blank and duplicate sibling names;
- `Cancel` and `Create`/`Rename` actions;
- Enter confirms and Escape cancels;
- no system `InputBox` and no direct configuration mutation in code-behind.

The dialog calls existing explorer methods only after validation. Persistence failures remain visible through the existing status reporting path.

### Settings

- Preserve the `General` and `Categories and rules` sections, but restyle tabs, cards, tree, grid, checkboxes, and buttons consistently.
- Add an `Updates` card to General settings containing:
  - installed version;
  - latest known version/status;
  - `Automatically check for updates` toggle;
  - `Check now` secondary action;
  - `Download and install` primary action when an update is available.
- Save the automatic-check preference through the existing settings transaction.

## Update Architecture

### Core contracts

Add platform-neutral types to `QuickSearch.Core`:

- `AppReleaseVersion` parses and compares stable semantic versions from `vMAJOR.MINOR.PATCH` tags.
- `UpdateRelease` contains version, release notes URL, installer asset URL, checksum asset URL, and installer file name.
- `IApplicationUpdateService` exposes check, download/verify, and launch-install operations.
- `UpdateViewModel` owns UI state, commands, status text, progress, automatic-check policy, and confirmation events.

Network and process details stay behind `IApplicationUpdateService`, so core tests use deterministic fakes.

### GitHub implementation

`GitHubReleaseUpdateService` in the Windows project:

1. Calls `https://api.github.com/repos/Createitv/quick-search/releases/latest` with a stable `User-Agent` and JSON accept header.
2. Rejects drafts, prereleases, malformed tags, missing assets, unexpected asset names, and non-HTTPS URLs.
3. Selects exactly:
   - `QuickSearch-Setup-v{version}.exe`
   - `QuickSearch-Setup-v{version}.exe.sha256`
4. Downloads into `%LOCALAPPDATA%\QuickSearch\updates\v{version}` using a temporary file and atomic rename.
5. Reads the sidecar, validates its exact 64-character hexadecimal form, computes SHA-256 over the installer, and compares in constant time.
6. Starts the verified installer with silent current-user arguments, then requests application shutdown.

The service deletes partial temporary downloads on failure but retains a successfully verified installer for retry.

### Version source

GitHub Actions passes the resolved tag version to `dotnet publish` as `Version`, `AssemblyVersion`, and `FileVersion`. Development builds use `0.0.0-dev`; tagged builds such as `v0.0.3` report `0.0.3` inside the running app.

`UpdateViewModel` reads the informational/file version through an injected current-version provider rather than hard-coding a version.

### Startup behavior

After configuration and Everything bootstrap initialization:

1. The main window becomes usable immediately.
2. If automatic checks are enabled and the last successful/attempted automatic check is older than 24 hours, start a cancellable update check.
3. No update: record the check time without showing a popup.
4. Update available: expose a compact non-modal update banner and update the settings card.
5. Network/API error: do not show a modal dialog; record a short status for settings. Manual checks surface the actionable error.

`LastUpdateCheckUtc` is persisted with app settings. A failed automatic check is also throttled to avoid repeated startup requests during an outage.

### Install and restart

The existing Inno Setup `AppId` remains unchanged, so the verified installer updates the current installation. The updater launches:

```text
/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /UPDATED
```

QuickSearch then exits. Inno Setup gains a silent-update run entry that starts `QuickSearch.exe --updated` after files are replaced. The application recognizes `--updated`, shows a one-time success status, and otherwise follows normal single-instance startup behavior.

Configuration is outside the install directory and is never deleted or replaced by the installer.

## Release Workflow

For tag and manual releases:

1. Resolve and validate `vMAJOR.MINOR.PATCH`.
2. Test and publish the self-contained Windows application with the resolved assembly/file version.
3. Build the Inno Setup installer.
4. Generate `QuickSearch-Setup-v{version}.exe.sha256` from the final installer.
5. Upload both files as the Actions artifact.
6. Upload both files to the matching GitHub Release.
7. Verify through the workflow that the release contains both non-empty assets.

Ordinary branch pushes continue creating CI artifacts but do not create public Releases.

## Migration and Compatibility

- Existing `v0.0.2` installations can run the next Inno Setup installer directly; no uninstall is required.
- Because `v0.0.2` has no updater, users must manually install the first bridge release containing this feature.
- After the bridge release, update checks and verified in-app installation are available.
- Existing configuration schema v2 remains readable. New update preferences have safe defaults when absent.
- Windows 10/11 x64 remain supported.

## Failure Handling

- GitHub unavailable or rate-limited: keep current version and show retry guidance only in settings/manual flow.
- Invalid release metadata: do not offer the update.
- Missing or malformed checksum: refuse installation.
- Hash mismatch: delete the invalid installer and report verification failure.
- Download cancellation: remove partial files and restore idle state.
- Installer launch failure: retain verified installer and allow retry.
- Application shutdown occurs only after `Process.Start` succeeds.

## Testing

### Core tests

- semantic-version parsing/comparison;
- update state transitions and command availability;
- automatic-check 24-hour throttle;
- no modal error for automatic failures;
- manual failure visibility;
- settings save/cancel for automatic checks;
- shutdown requested only after a successful installer launch.

### Windows service tests

- GitHub JSON parsing and asset selection using an injected HTTP handler;
- draft/prerelease/malformed release rejection;
- download progress and cancellation;
- checksum success, malformed checksum, mismatch, and partial-file cleanup;
- exact installer arguments.

### Delivery verification

- existing core suite passes;
- Windows Release build compiles WPF UI resources and dialogs;
- tagged GitHub Actions run publishes installer plus checksum;
- install bridge version over `v0.0.2`, retain configuration, check for a higher test release, apply it, restart, and confirm the new displayed version.

## Out of Scope

- unattended installation without user confirmation;
- private GitHub repository authentication;
- downgrade UI;
- beta/update channels;
- delta packages;
- Velopack migration;
- Windows code-signing certificate acquisition. The workflow should remain ready for signing to be added later.
