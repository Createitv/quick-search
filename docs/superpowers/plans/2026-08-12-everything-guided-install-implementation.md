# Everything Guided Installation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect an installed Everything client, start it invisibly when needed, and show an in-app launcher for the bundled official installer only when Everything is absent.

**Architecture:** Add testable bootstrap state and orchestration to `QuickSearch.Core`, while keeping registry, process, and SHA-256 operations in a focused Windows service. `App` initializes the bootstrap before normal launcher activation, and `MainWindow` binds a full-width installation band to the bootstrap view model. CI pins, verifies, and packages the official Everything x64 installer.

**Tech Stack:** .NET 8, C# 12, WPF, xUnit, Windows Registry, `System.Diagnostics.Process`, SHA-256, Inno Setup, GitHub Actions PowerShell.

## Global Constraints

- Support Windows 10/11 x64 and Everything 1.4 x64 normal edition.
- Show the official Everything installation wizard; do not perform silent installation.
- Show installation UI only when no usable Everything installation is found.
- Start installed Everything with `-startup` so no search window appears.
- Do not uninstall Everything when QuickSearch is removed.
- Do not support Everything Lite because it has no IPC.
- Do not commit the Everything installer binary to source control.
- Preserve the user's unrelated change in `docs/superpowers/plans/2026-07-10-scalable-mapping-manager-search-results-implementation.md`.

---

## File Structure

- Create `src/QuickSearch.Core/EverythingBootstrap.cs`: platform-neutral bootstrap states, result records, service interface, and orchestration view model.
- Create `src/QuickSearch.Windows/Services/EverythingInstallationManager.cs`: registry discovery, bundled installer verification, process launch, and readiness polling.
- Create `tests/QuickSearch.Core.Tests/EverythingBootstrapTests.cs`: bootstrap state-transition tests with a fake platform service.
- Modify `src/QuickSearch.Windows/App.xaml.cs`: compose and initialize bootstrap services before launcher activation.
- Modify `src/QuickSearch.Windows/MainWindow.xaml`: bind the install band and disable search commands until ready.
- Modify `src/QuickSearch.Windows/MainWindow.xaml.cs`: accept and dispose bootstrap view model.
- Modify `src/QuickSearch.Windows/QuickSearch.Windows.csproj`: package the installer hash manifest.
- Modify `.github/workflows/windows-release.yml`: download a pinned official installer, verify SHA-256, and copy it into publish output.
- Modify `packaging/QuickSearch.iss`: include the dependency directory through existing recursive publish packaging and verify it at compile time.
- Modify `THIRD-PARTY-NOTICES.txt` and `README.md`: document the bundled official installer and automatic bootstrap behavior.

### Task 1: Bootstrap State Machine

**Files:**
- Create: `src/QuickSearch.Core/EverythingBootstrap.cs`
- Create: `tests/QuickSearch.Core.Tests/EverythingBootstrapTests.cs`

**Interfaces:**
- Produces: `IEverythingInstallationManager.CheckAndStartAsync(CancellationToken)`, `InstallAndStartAsync(CancellationToken)`, `EverythingBootstrapState`, and `EverythingBootstrapViewModel`.
- Consumes: existing `ObservableObject` and command patterns in `ViewModelCommands.cs`.

- [ ] **Step 1: Write failing state-transition tests**

Test a ready startup, missing installation, successful installation, failed installation, and concurrent install rejection. Use a fake `IEverythingInstallationManager` returning queued `EverythingBootstrapResult` values and assert `State`, `IsInstallPanelVisible`, `CanSearch`, `StatusText`, and call counts.

```csharp
[Fact]
public async Task InitializeAsync_WhenEverythingIsMissing_ShowsInstaller()
{
    var manager = new FakeInstallationManager(
        new EverythingBootstrapResult(EverythingBootstrapState.NotInstalled, "需要安装 Everything。"));
    var viewModel = new EverythingBootstrapViewModel(manager);

    await viewModel.InitializeAsync();

    Assert.True(viewModel.IsInstallPanelVisible);
    Assert.False(viewModel.CanSearch);
    Assert.Equal(EverythingBootstrapState.NotInstalled, viewModel.State);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --filter EverythingBootstrapTests`

Expected: compilation fails because the bootstrap types do not exist.

- [ ] **Step 3: Implement the minimal bootstrap model**

Define immutable results and an async command. `InitializeAsync` delegates to `CheckAndStartAsync`; `InstallAsync` guards re-entry, exposes `Installing`, delegates to `InstallAndStartAsync`, and always maps the returned result into observable UI properties.

```csharp
public enum EverythingBootstrapState
{
    Checking, Ready, NotInstalled, Starting, Installing,
    InstallerMissing, InstallerInvalid, Failed
}

public sealed record EverythingBootstrapResult(
    EverythingBootstrapState State,
    string Message);
```

- [ ] **Step 4: Run focused and full core tests**

Run: `dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --filter EverythingBootstrapTests`

Expected: all bootstrap tests pass.

Run: `dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj`

Expected: all core tests pass.

- [ ] **Step 5: Commit the state machine**

```bash
git add src/QuickSearch.Core/EverythingBootstrap.cs tests/QuickSearch.Core.Tests/EverythingBootstrapTests.cs
git commit -m "feat: add Everything bootstrap state"
```

### Task 2: Windows Everything Installation Manager

**Files:**
- Create: `src/QuickSearch.Windows/Services/EverythingInstallationManager.cs`
- Create: `src/QuickSearch.Windows/Services/EverythingInstallationPaths.cs`
- Test: `tests/QuickSearch.Core.Tests/EverythingInstallationPathsTests.cs`

**Interfaces:**
- Consumes: `IEverythingInstallationManager`, `EverythingBootstrapResult`, and `IEverythingNative`.
- Produces: `EverythingInstallationManager(IEverythingNative native, string applicationDirectory)`.

- [ ] **Step 1: Write failing candidate-path tests**

Test that registered display icon values have quotes and `,0` removed, executable registry values are retained, duplicate paths are removed case-insensitively, and Program Files candidates are appended.

```csharp
[Theory]
[InlineData("\"C:\\Program Files\\Everything\\Everything.exe\",0", "C:\\Program Files\\Everything\\Everything.exe")]
public void NormalizeExecutablePath_RemovesDisplayIconDecoration(string input, string expected)
{
    Assert.Equal(expected, EverythingInstallationPaths.NormalizeExecutablePath(input));
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --filter EverythingInstallationPathsTests`

Expected: compilation fails because `EverythingInstallationPaths` does not exist.

- [ ] **Step 3: Implement path normalization and Windows manager**

The manager must probe IPC first, discover normal-edition executables from HKLM/HKCU uninstall keys in 32/64-bit views plus standard directories, launch exactly one process with `-startup`, and poll `_native.IsDatabaseLoaded()` with a bounded delay. Installer execution must verify `dependencies/Everything-Setup.exe` against `dependencies/Everything-Setup.sha256`, launch with `UseShellExecute = true`, await exit asynchronously, then rediscover instead of trusting exit code.

```csharp
var startInfo = new ProcessStartInfo(executablePath, "-startup")
{
    UseShellExecute = true
};
```

- [ ] **Step 4: Run tests and compile the Windows project**

Run: `dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --filter EverythingInstallationPathsTests`

Expected: all path tests pass.

Run: `dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release`

Expected: build succeeds without warnings or errors.

- [ ] **Step 5: Commit Windows integration**

```bash
git add src/QuickSearch.Windows/Services/EverythingInstallationManager.cs src/QuickSearch.Windows/Services/EverythingInstallationPaths.cs tests/QuickSearch.Core.Tests/EverythingInstallationPathsTests.cs
git commit -m "feat: discover and start Everything"
```

### Task 3: Application Composition And Installation UI

**Files:**
- Modify: `src/QuickSearch.Windows/App.xaml.cs`
- Modify: `src/QuickSearch.Windows/MainWindow.xaml`
- Modify: `src/QuickSearch.Windows/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `EverythingBootstrapViewModel` and `EverythingInstallationManager`.
- Produces: a main-window install band bound to `Bootstrap.IsInstallPanelVisible`, `Bootstrap.StatusText`, and `Bootstrap.InstallCommand`.

- [ ] **Step 1: Add a failing composition boundary test**

Extend `PlatformBoundaryTests` with a small binding-context contract asserting a host object exposes both launcher and bootstrap instances, preventing the new WPF data context from dropping existing launcher bindings.

- [ ] **Step 2: Run the test and verify RED**

Run: `dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --filter PlatformBoundaryTests`

Expected: fails because `MainWindowViewModel` does not exist.

- [ ] **Step 3: Implement composition and XAML states**

Create `MainWindowViewModel` in the core bootstrap file with `Launcher` and `Bootstrap` properties. Update all existing XAML bindings to `Launcher.*`, add a full-width install band with one primary install button, and bind search controls to `Bootstrap.CanSearch`. Initialize bootstrap after the window exists; show the window when installation is required and preserve existing clipboard activation when ready.

- [ ] **Step 4: Run core tests and Windows build**

Run: `dotnet test QuickSearch.sln -c Release`

Expected: all tests pass.

Run: `dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release`

Expected: XAML compilation and Windows build pass.

- [ ] **Step 5: Commit UI integration**

```bash
git add src/QuickSearch.Windows/App.xaml.cs src/QuickSearch.Windows/MainWindow.xaml src/QuickSearch.Windows/MainWindow.xaml.cs src/QuickSearch.Core/EverythingBootstrap.cs tests/QuickSearch.Core.Tests/PlatformBoundaryTests.cs
git commit -m "feat: guide Everything installation"
```

### Task 4: Pinned Installer Packaging

**Files:**
- Modify: `.github/workflows/windows-release.yml`
- Modify: `src/QuickSearch.Windows/QuickSearch.Windows.csproj`
- Modify: `packaging/QuickSearch.iss`
- Modify: `THIRD-PARTY-NOTICES.txt`
- Modify: `README.md`

**Interfaces:**
- Consumes: official Everything x64 setup URL and its verified SHA-256.
- Produces: `dependencies/Everything-Setup.exe` and `dependencies/Everything-Setup.sha256` in publish and installed output.

- [ ] **Step 1: Resolve and independently verify the pinned official artifact**

Download the official Everything 1.4 x64 setup from voidtools, calculate SHA-256 twice from the downloaded file, and record the exact version, URL, size, and hash. Abort if the filename/version is not the normal x64 installer.

- [ ] **Step 2: Add CI download, hash enforcement, and publish assertions**

PowerShell must compare the calculated hash with a literal expected hash before copying the installer and writing the lowercase hash manifest.

```powershell
$actual = (Get-FileHash Everything-Setup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "Everything installer SHA-256 mismatch" }
```

- [ ] **Step 3: Update project packaging and documentation**

Include `dependencies/**` in publish output when present, fail Inno compilation when the files are missing, document automatic startup and guided installation in README, and add the pinned application installer copyright/source/license notice without changing the existing SDK notice.

- [ ] **Step 4: Validate packaging configuration**

Run: `dotnet test QuickSearch.sln -c Release`

Expected: all tests pass.

Run: `dotnet publish src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish`

Expected: application publishes successfully; the dependency check is exercised after the CI download step on Windows.

Run: `git diff --check`

Expected: no whitespace errors.

- [ ] **Step 5: Commit packaging changes**

```bash
git add .github/workflows/windows-release.yml src/QuickSearch.Windows/QuickSearch.Windows.csproj packaging/QuickSearch.iss THIRD-PARTY-NOTICES.txt README.md
git commit -m "build: bundle official Everything installer"
```

### Task 5: End-To-End Verification

**Files:**
- Modify only files required by verification findings.

**Interfaces:**
- Consumes: completed Tasks 1-4.
- Produces: verified release-ready behavior and an evidence summary.

- [ ] **Step 1: Run all automated checks**

Run: `dotnet test QuickSearch.sln -c Release --verbosity minimal`

Run: `dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release --verbosity minimal`

Run: `git diff --check`

Expected: every command succeeds.

- [ ] **Step 2: Inspect the release diff**

Confirm no secrets or installer binary were committed, no unrelated user changes were staged, no Everything uninstall behavior was added, and all process launches use explicit executable paths and arguments.

- [ ] **Step 3: Verify on Windows**

On a clean Windows 10/11 x64 environment, validate absent/install/cancel/retry/success. On an installed environment, validate invisible `-startup`, automatic IPC readiness, and no install panel. Record any Windows-only verification that cannot be performed from macOS rather than claiming it passed.

- [ ] **Step 4: Commit verification fixes if needed**

```bash
git add <only-files-changed-by-verification>
git commit -m "fix: harden Everything bootstrap"
```

