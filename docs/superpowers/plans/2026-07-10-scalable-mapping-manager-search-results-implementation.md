# Scalable Mapping Manager and Dense Search Results Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Group all keywords for one folder into a comma-separated mapping row, add searchable virtualized mapping management, and show more real-folder results on the launcher.

**Architecture:** Add a focused `MappingGroupEditorViewModel` that parses display text into ordered unique keywords. `SettingsViewModel` edits folder groups but still persists the existing flat `FolderMapping` JSON schema by diffing desired keyword/path pairs against the current configuration. WPF uses a full-page virtualized table, while the launcher uses a larger window and compact two-column results.

**Tech Stack:** .NET 8, C# 12, WPF, xUnit, System.Text.Json, Everything 1.4 SDK.

## Global Constraints

- Keep the existing JSON schema and old mapping data readable without migration.
- Preserve many-to-many semantics: one keyword may target multiple folders and one folder may have multiple keywords.
- Accept English/Chinese commas, English/Chinese semicolons, and newlines as keyword separators.
- Keep settings changes transactional until “保存更改”.
- Use a searchable virtualized table without pagination or CSV import/export.
- Keep Everything folder-only search, the 20-result cap, and existing keyboard shortcuts.
- Do not append `Co-Authored-By` trailers to commits.

---

### Task 1: Folder Mapping Group Model

**Files:**
- Create: `src/QuickSearch.Core/MappingGroupEditorViewModel.cs`
- Create: `tests/QuickSearch.Core.Tests/MappingGroupEditorViewModelTests.cs`

**Interfaces:**
- Produces: `MappingGroupEditorViewModel(IEnumerable<string> keywords, string folderPath)`.
- Produces: `string KeywordsText`, `string FolderPath`.
- Produces: `IReadOnlyList<string> ParseKeywords()`.
- Produces: `bool Matches(string filter)`.

- [ ] **Step 1: Write parser and filter tests**

```csharp
[Fact]
public void ParseKeywords_AcceptsAllSeparatorsAndKeepsFirstDisplayForm()
{
    var group = new MappingGroupEditorViewModel(
        "你好，今天好; hello\nHELLO； 你好 ",
        @"D:\资料");

    Assert.Equal(["你好", "今天好", "hello"], group.ParseKeywords());
}
```

Also test empty removal, internal-whitespace normalization for deduplication, keyword matching, and case-insensitive path matching.

- [ ] **Step 2: Run focused test and verify RED**

Run: `/tmp/quick-search-dotnet/dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MappingGroupEditorViewModelTests`

Expected: compilation fails because `MappingGroupEditorViewModel` does not exist.

- [ ] **Step 3: Implement the parser**

Split on `,，;；\r\n`, trim each value, discard blanks, and deduplicate with `AliasNormalizer.Normalize` while preserving first-seen display text and order. The enumerable constructor joins keywords as `， `.

```csharp
public IReadOnlyList<string> ParseKeywords()
{
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var result = new List<string>();
    foreach (var value in KeywordsText.Split(Separators, StringSplitOptions.TrimEntries))
    {
        if (value.Length > 0 && seen.Add(AliasNormalizer.Normalize(value)))
            result.Add(value);
    }
    return result;
}
```

- [ ] **Step 4: Verify GREEN and commit**

Run the focused tests, then commit:

```bash
git add src/QuickSearch.Core/MappingGroupEditorViewModel.cs tests/QuickSearch.Core.Tests/MappingGroupEditorViewModelTests.cs
git commit -m "feat: add grouped keyword mapping editor"
```

### Task 2: Searchable Transactional Mapping Groups

**Files:**
- Modify: `src/QuickSearch.Core/SettingsViewModel.cs`
- Modify: `tests/QuickSearch.Core.Tests/SettingsViewModelTests.cs`
- Delete: `src/QuickSearch.Core/MappingEditorViewModel.cs` after all consumers migrate.

**Interfaces:**
- Consumes: `MappingGroupEditorViewModel` from Task 1.
- Produces: `ObservableCollection<MappingGroupEditorViewModel> MappingGroups`.
- Produces: `IReadOnlyList<MappingGroupEditorViewModel> FilteredMappingGroups`.
- Produces: `string MappingFilter`, `string MappingCountText`.
- Produces: `MappingGroupEditorViewModel? SelectedMappingGroup`.
- Existing `AddMappingCommand` and `DeleteMappingCommand` operate on groups.

- [ ] **Step 1: Replace flat-row tests with group behavior tests**

Cover:

```text
flat configuration rows sharing a path -> one group with comma-separated keywords
filter matches keyword text and folder path
200 groups -> correct visible/total count
new group -> inserted at index 0 and selected
filtered delete -> removes the selected source group
three keywords in one group -> three persisted flat mappings
same keyword in two folder groups -> two persisted mappings
cancel -> restores original groups
persistence failure -> rolls back config and platform state
```

- [ ] **Step 2: Verify RED**

Run `SettingsViewModelTests`. Expected: compilation fails for group/filter properties and old code still exposes flat `Mappings`.

- [ ] **Step 3: Build groups on edit**

Group `_configuration.Mappings` by trimmed folder path using `StringComparer.OrdinalIgnoreCase`, preserve the first path spelling, and collect aliases in configuration order. Subscribe to group `PropertyChanged` so edits refresh the filter and count.

- [ ] **Step 4: Implement filtering and group commands**

`MappingFilter` calls `RefreshFilteredMappingGroups`. New groups are inserted at index 0. Delete removes `SelectedMappingGroup` from the source collection even when a filter is active.

- [ ] **Step 5: Persist desired pairs without changing JSON**

Build a normalized desired pair dictionary from every group’s parsed keywords and path. Clone the current configuration, remove existing pairs absent from the desired dictionary, and add desired pairs that do not exist. This preserves timestamps for unchanged pairs and creates metadata only for new pairs.

- [ ] **Step 6: Run settings and full core tests**

Expected: all settings transaction tests and the complete core suite pass.

- [ ] **Step 7: Commit grouped settings behavior**

```bash
git add src/QuickSearch.Core/SettingsViewModel.cs src/QuickSearch.Core/MappingEditorViewModel.cs tests/QuickSearch.Core.Tests/SettingsViewModelTests.cs
git commit -m "feat: manage mappings by folder group"
```

### Task 3: Full-Page Virtualized Mapping UI

**Files:**
- Modify: `src/QuickSearch.Windows/SettingsWindow.xaml`
- Modify: `src/QuickSearch.Windows/SettingsWindow.xaml.cs`
- Modify: `src/QuickSearch.Windows/Themes/ModernTheme.xaml`

**Interfaces:**
- Consumes: grouped/filter properties and existing commands from Task 2.
- Produces: two settings tabs, searchable count-aware mapping page, virtualized inline-edit table, Ctrl+F focus, Delete removal.

- [ ] **Step 1: Add intentional tab and table styles**

Extend `ModernTheme.xaml` with `SettingsTabItemStyle`, `MappingDataGridStyle`, header/cell/row styles derived from existing Paper, Ink, Slate, Line, RouteBlue, and SignalCyan resources. Keep visible keyboard focus and compact 42-pixel rows.

- [ ] **Step 2: Recompose the settings window**

Change default size to `980 × 720`, minimum `780 × 580`. Put general settings in one tab and mapping management in another. The mapping toolbar binds filter/count/buttons. A `DataGrid` binds `FilteredMappingGroups` with template columns for `KeywordsText` and `FolderPath`.

Use:

```xml
EnableRowVirtualization="True"
EnableColumnVirtualization="True"
VirtualizingPanel.IsVirtualizing="True"
VirtualizingPanel.VirtualizationMode="Recycling"
ScrollViewer.CanContentScroll="True"
```

- [ ] **Step 3: Add keyboard focus behavior**

Handle window preview keys in code-behind: Ctrl+F selects the mapping tab and focuses `MappingFilterBox`. Bind Delete on the grid to `DeleteMappingCommand`. Business mutations remain in the view model.

- [ ] **Step 4: Build Windows project**

Run: `/tmp/quick-search-dotnet/dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true`

Expected: 0 warnings and 0 errors.

- [ ] **Step 5: Commit settings UI**

```bash
git add src/QuickSearch.Windows/SettingsWindow.xaml src/QuickSearch.Windows/SettingsWindow.xaml.cs src/QuickSearch.Windows/Themes/ModernTheme.xaml
git commit -m "feat(ui): add scalable mapping manager"
```

### Task 4: Dense Real-Folder Search Results

**Files:**
- Modify: `src/QuickSearch.Windows/MainWindow.xaml`
- Modify: `README.md`

**Interfaces:**
- Consumes: existing launcher search, selection, and open commands.
- Produces: resizable `840 × 720` launcher with compact two-column rows and full-path tooltips.

- [ ] **Step 1: Expand the result viewport**

Set the default window to `840 × 720` with a practical minimum. Reduce status/search margins and make results retain the star-sized row.

- [ ] **Step 2: Make each result compact**

Use a two-column grid with a bounded name column and a path column. Set path `TextWrapping="NoWrap"`, `TextTrimming="CharacterEllipsis"`, and `ToolTip="{Binding FullPath}"`. Keep Enter/double-click bindings unchanged.

- [ ] **Step 3: Update README**

Explain comma-separated multi-keyword groups, mapping filter, and the larger real-folder search page.

- [ ] **Step 4: Build and commit**

Run the Windows Werror build, then:

```bash
git add src/QuickSearch.Windows/MainWindow.xaml README.md
git commit -m "feat(ui): show more folder search results"
```

### Task 5: Final Verification

**Files:** No planned production changes.

- [ ] **Step 1: Verify formatting**

Run: `/tmp/quick-search-dotnet/dotnet format QuickSearch.sln --verify-no-changes --no-restore --verbosity minimal`

- [ ] **Step 2: Run all tests**

Run: `/tmp/quick-search-dotnet/dotnet test QuickSearch.sln -c Release --no-restore --verbosity minimal`

Expected: 0 failed and 0 skipped.

- [ ] **Step 3: Run Windows build and smoke test**

```bash
/tmp/quick-search-dotnet/dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true
dotnet run --project tools/QuickSearch.Smoke/QuickSearch.Smoke.csproj -c Release
```

Expected: 0 warnings, 0 errors, and `QuickSearch configuration smoke check passed.`

- [ ] **Step 4: Check repository state**

Run: `git diff --check origin/feature/quick-search..HEAD && git status --short --branch`

Expected: no whitespace errors and a clean feature branch ahead of its remote.

