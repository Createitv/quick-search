# Unified Launcher and Multi-Folder Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn QuickSearch into one keyword launcher that directly opens every mapped folder and falls back to an Everything folder search when no mapping exists.

**Architecture:** Keep mappings as a flat JSON list but make `(normalized keyword, case-insensitive path)` the unique key. `LauncherViewModel` becomes the single decision point for clipboard activation, multi-folder opening, and Everything search; the WPF window only decides whether the activation result requires showing the launcher.

**Tech Stack:** .NET 8, C# 12, WPF, xUnit, Everything 1.4 SDK, System.Text.Json.

## Global Constraints

- Keep the existing `config.json` schema readable without a migration or data loss.
- Reuse the current configurable global shortcut; do not add a second shortcut or mode switch.
- A mapped keyword opens every valid mapped folder immediately with no confirmation window.
- An unmapped clipboard keyword opens the launcher and searches folders through Everything.
- An empty clipboard opens an empty, focused search box.
- Search results open individually with Up/Down selection, Enter, or double-click and never create mappings automatically.
- Keep the result cap at 20 and never add recursive disk scanning.
- Preserve transactional settings saves and close-to-tray behavior.
- Do not append `Co-Authored-By` trailers to commits.

---

### Task 1: One-to-Many Mapping Domain

**Files:**
- Modify: `src/QuickSearch.Core/AppConfiguration.cs`
- Modify: `src/QuickSearch.Core/MappingEditorViewModel.cs`
- Modify: `tests/QuickSearch.Core.Tests/AppConfigurationTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<FolderMapping> FindMappings(string keyword)`.
- Produces: `FolderMapping AddMapping(string keyword, string folderPath)`.
- Produces: `bool RemoveMapping(string keyword, string folderPath)`.
- Produces: `FolderMapping UpdateMapping(string originalKeyword, string originalPath, string keyword, string folderPath)`.
- Produces: `FolderMapping? MarkMappingUsed(string keyword, string folderPath)`.

- [ ] **Step 1: Write failing one-to-many tests**

Add tests proving construction keeps two different paths for one normalized keyword, folds only exact keyword/path duplicates, `FindMappings` returns all paths, and add/update/remove/mark-used target one row without affecting siblings.

```csharp
[Fact]
public void AddMapping_AllowsOneKeywordToTargetMultipleFolders()
{
    var configuration = new AppConfiguration();
    configuration.AddMapping("project alpha", @"C:\Alpha");
    configuration.AddMapping(" PROJECT\tALPHA ", @"D:\Archive\Alpha");

    Assert.Equal(2, configuration.FindMappings("project alpha").Count);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run: `/tmp/quick-search-dotnet/dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AppConfigurationTests`

Expected: compilation fails because `AddMapping` and `FindMappings` do not exist and current initialization collapses same-keyword rows.

- [ ] **Step 3: Implement composite-key mapping operations**

Change initialization deduplication from normalized keyword only to normalized keyword plus case-insensitive trimmed path. Keep `FindMapping` and the old overloads as compatibility wrappers where useful, but route all new behavior through the composite-key methods. Add `OriginalFolderPath` to `MappingEditorViewModel` so settings edits can identify a single original row.

```csharp
public IReadOnlyList<FolderMapping> FindMappings(string keyword) => _mappings
    .Where(mapping => mapping.NormalizedAlias == AliasNormalizer.Normalize(keyword))
    .ToArray();

public bool RemoveMapping(string keyword, string folderPath) =>
    _mappings.RemoveAll(mapping => IsSameMapping(mapping, keyword, folderPath)) > 0;
```

- [ ] **Step 4: Run focused and full core tests**

Expected: focused tests pass. Update legacy overwrite assertions to the explicit single-row update API while preserving every unrelated behavior test.

- [ ] **Step 5: Commit the domain change**

```bash
git add src/QuickSearch.Core/AppConfiguration.cs src/QuickSearch.Core/MappingEditorViewModel.cs tests/QuickSearch.Core.Tests/AppConfigurationTests.cs
git commit -m "feat: support multiple folders per keyword"
```

### Task 2: Transactional Manual Multi-Mapping Settings

**Files:**
- Modify: `src/QuickSearch.Core/SettingsViewModel.cs`
- Modify: `tests/QuickSearch.Core.Tests/SettingsViewModelTests.cs`
- Modify: `src/QuickSearch.Windows/SettingsWindow.xaml`

**Interfaces:**
- Consumes: composite-key mapping APIs from Task 1.
- Produces: settings rows that allow repeated keywords with different paths and reject only fully duplicate rows.

- [ ] **Step 1: Write failing settings tests**

Add tests that save two rows with the same normalized keyword and different paths, reject two rows with the same normalized keyword and case-insensitive path, edit one sibling without changing the other, and delete only the selected sibling.

```csharp
[Fact]
public async Task SaveAsync_AllowsRepeatedKeywordWithDifferentPaths()
{
    var viewModel = CreateViewModel(CreateConfiguration(), new FakeMappingStore());
    viewModel.AddMapping();
    viewModel.Mappings[^1].Alias = " SALES ";
    viewModel.Mappings[^1].FolderPath = "/sales-archive";

    await viewModel.SaveAsync();

    Assert.Equal(2, viewModel.Mappings.Count);
}
```

- [ ] **Step 2: Verify RED**

Run the settings test class only. Expected: save reports `映射别名重复` and the second mapping is not persisted.

- [ ] **Step 3: Implement row-specific candidate building and validation**

Track retained rows by original keyword/path pair, call the row-specific remove and update overloads, and validate a composite normalized-keyword/path key rather than keyword alone. Change copy from “邮箱别名” to “关键词”, keeping the existing Add/Delete/Save controls and layout.

- [ ] **Step 4: Verify focused and rollback tests**

Run all `SettingsViewModelTests`. Expected: new multi-map tests and existing hotkey/startup/persistence rollback tests all pass.

- [ ] **Step 5: Commit settings support**

```bash
git add src/QuickSearch.Core/SettingsViewModel.cs tests/QuickSearch.Core.Tests/SettingsViewModelTests.cs src/QuickSearch.Windows/SettingsWindow.xaml
git commit -m "feat: manage multi-folder keyword mappings"
```

### Task 3: Mapping-First Clipboard Activation and Search Fallback

**Files:**
- Create: `src/QuickSearch.Core/LauncherActivationDisposition.cs`
- Modify: `src/QuickSearch.Core/LauncherViewModel.cs`
- Modify: `tests/QuickSearch.Core.Tests/LauncherViewModelTests.cs`

**Interfaces:**
- Produces: `Task<LauncherActivationDisposition> ActivateFromClipboardAsync()`.
- Produces: `SearchText`, `Results`, `SelectedResult`, `OpenSelectedCommand`, and `SearchCommand`.
- `LauncherActivationDisposition.OpenedMappings` means the window stays hidden.
- `LauncherActivationDisposition.ShowLauncher` means WPF shows and focuses the search window.

- [ ] **Step 1: Write failing activation tests**

Cover these independent behaviors:

```text
mapped keyword + two valid paths -> opens both, saves usage, returns OpenedMappings
mapped keyword + one invalid path -> opens valid path, returns ShowLauncher with error
mapped keyword + all invalid paths -> searches clipboard keyword, returns ShowLauncher
unmapped clipboard keyword -> searches it immediately, returns ShowLauncher
empty clipboard -> clears stale query/results, returns ShowLauncher
clipboard read failure -> clears stale query and returns ShowLauncher with error
```

Also prove opening a normal Everything result does not call `SaveAsync` or add a mapping.

- [ ] **Step 2: Verify RED**

Run `LauncherViewModelTests`. Expected: compilation fails for the async activation API/disposition, and legacy confirmation behavior persists mappings.

- [ ] **Step 3: Implement the unified launcher state machine**

Replace alias-resolution state with one `SearchText` state. Keep `Alias` and `FolderQuery` as temporary compatibility aliases if that makes the test migration safer, but bind the UI only to `SearchText`. Activation reads and normalizes clipboard text, finds every mapping, opens all valid paths, marks only successful rows used, and returns a disposition. The search fallback awaits `SearchNowAsync` so results are ready when the window appears.

```csharp
public enum LauncherActivationDisposition
{
    OpenedMappings,
    ShowLauncher
}
```

`OpenSelectedAsync` opens only `SelectedResult.FullPath`, hides on success, and never modifies `Configuration.Mappings`.

- [ ] **Step 4: Verify focused tests and search cancellation regressions**

Run all launcher and Everything search tests. Expected: multi-open, fallback, selection, cancellation, stale-result, and error-state tests pass.

- [ ] **Step 5: Commit launcher behavior**

```bash
git add src/QuickSearch.Core/LauncherActivationDisposition.cs src/QuickSearch.Core/LauncherViewModel.cs tests/QuickSearch.Core.Tests/LauncherViewModelTests.cs
git commit -m "feat: add mapping-first unified launcher"
```

### Task 4: Single-Input WPF Launcher and Keyboard Flow

**Files:**
- Modify: `src/QuickSearch.Windows/MainWindow.xaml`
- Modify: `src/QuickSearch.Windows/MainWindow.xaml.cs`
- Modify: `src/QuickSearch.Windows/App.xaml.cs`
- Modify: `src/QuickSearch.Windows/Themes/ModernTheme.xaml` only if an existing style cannot express the approved layout.

**Interfaces:**
- Consumes: activation disposition and unified launcher properties from Task 3.
- Produces: one focused search box, result selection with Up/Down, Enter and double-click open, Escape hide.

- [ ] **Step 1: Convert window activation to async disposition handling**

`MainWindow.ActivateFromClipboardAsync` awaits the view model. It hides or remains hidden for `OpenedMappings`, and calls `ShowLauncher` only for `ShowLauncher`. Update hotkey, startup, and single-instance callbacks to dispatch the async method safely.

- [ ] **Step 2: Recompose `MainWindow.xaml` around one search box**

Remove the alias-to-folder confirmation beam. Keep the existing Path Beam palette and card/list styles, but present:

```text
QuickSearch + Settings
[ Search folders with Everything                         ]
status
folder results list
Esc hide | Up/Down select | Enter open
```

Bind the search box to `SearchText`. Bind Enter on the result list and the primary button to `OpenSelectedCommand`. Let the WPF `ListBox` handle Up/Down selection natively.

- [ ] **Step 3: Build the Windows project with warnings as errors**

Run: `/tmp/quick-search-dotnet/dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true`

Expected: 0 warnings, 0 errors; XAML bindings and named controls compile.

- [ ] **Step 4: Commit the WPF flow**

```bash
git add src/QuickSearch.Windows/MainWindow.xaml src/QuickSearch.Windows/MainWindow.xaml.cs src/QuickSearch.Windows/App.xaml.cs src/QuickSearch.Windows/Themes/ModernTheme.xaml
git commit -m "feat(ui): simplify QuickSearch to one search input"
```

### Task 5: Documentation and Final Verification

**Files:**
- Modify: `README.md`
- Modify: `.superpowers/sdd/task-3-report.md` only if its current-behavior statements need correction.

- [ ] **Step 1: Update user documentation**

Document keyword-to-many-folder mappings, direct opening with the global shortcut, Everything fallback, empty-clipboard search, keyboard controls, and manual settings rows. Remove claims that every saved mapping requires confirmation.

- [ ] **Step 2: Run format verification**

Run: `/tmp/quick-search-dotnet/dotnet format QuickSearch.sln --verify-no-changes --no-restore --verbosity minimal`

Expected: exit 0. If formatting changes are required, run format, inspect the named files, and rerun verification.

- [ ] **Step 3: Run all tests and smoke verification**

```bash
/tmp/quick-search-dotnet/dotnet test QuickSearch.sln -c Release --no-restore --verbosity minimal
dotnet run --project tools/QuickSearch.Smoke/QuickSearch.Smoke.csproj -c Release
```

Expected: all tests pass with zero skipped tests; smoke output is `QuickSearch configuration smoke check passed.`

- [ ] **Step 4: Run final Windows Release build and diff checks**

```bash
/tmp/quick-search-dotnet/dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true
git diff --check
git status --short --branch
```

Expected: build succeeds with 0 warnings and 0 errors, diff check is clean, and only intentional files remain.

- [ ] **Step 5: Commit documentation**

```bash
git add README.md .superpowers/sdd/task-3-report.md
git commit -m "docs: explain unified keyword launcher"
```
