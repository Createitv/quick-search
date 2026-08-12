# QuickSearch Hierarchical Rule Explorer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the search-form home screen with a persistent hierarchical rule explorer while preserving clipboard shortcut opening and Everything folder search.

**Architecture:** Upgrade persisted configuration to schema version 2 with stable category and rule IDs, while keeping `AppConfiguration.FindMappings` as the compatibility boundary for existing launcher behavior. Add a focused `RuleExplorerViewModel` for navigation, pins, local search, and lightweight management; keep Everything and path opening in `LauncherViewModel`, and give settings a transactional `NavigationOrganizerViewModel` for full tree and rule management.

**Tech Stack:** .NET 8, C# 12, WPF/XAML, xUnit 2.5.3, System.Text.Json

## Global Constraints

- Preserve existing clipboard keyword behavior: a matching keyword opens all valid target folders without showing a confirmation form.
- Preserve Everything as a folder-only search fallback with at most 20 results, arrow-key selection, `Enter`, double-click, and recoverable health errors.
- A rule has exactly one category and one physical target path; a keyword may still target multiple rules.
- Category depth has no fixed limit; category parent relationships must remain acyclic.
- Any category may be pinned, and pin order must persist without duplicating categories or rules.
- Home screen lightweight edits save atomically and roll back on failure; settings edits remain staged until `Save changes`.
- Remove the in-content `QuickSearch` title and clipboard-alias copy; keep the settings gear in the bottom-right status bar.
- Preserve Windows 10/11 compatibility, keyboard focus visibility, reduced-motion behavior, and the existing Path Beam palette.
- Do not stage or commit existing changes to `README.md`, the July implementation plan, the Superpowers update plan, or `scripts/`.
- Do not append `Co-Authored-By` trailers to commits.

---

### Task 1: Schema v2 rule catalog and deterministic migration

**Files:**
- Create: `src/QuickSearch.Core/NavigationFolder.cs`
- Create: `src/QuickSearch.Core/FolderRule.cs`
- Create: `src/QuickSearch.Core/ConfigurationMigrator.cs`
- Modify: `src/QuickSearch.Core/AppSettings.cs`
- Modify: `src/QuickSearch.Core/AppConfiguration.cs`
- Modify: `src/QuickSearch.Core/JsonMappingStore.cs`
- Create: `tests/QuickSearch.Core.Tests/ConfigurationMigratorTests.cs`
- Modify: `tests/QuickSearch.Core.Tests/AppConfigurationTests.cs`
- Modify: `tests/QuickSearch.Core.Tests/JsonMappingStoreTests.cs`

**Interfaces:**
- Produces: `NavigationFolder(Guid Id, Guid? ParentId, string Name, int SortOrder, DateTimeOffset UpdatedAtUtc)`.
- Produces: `FolderRule(Guid Id, string Title, IReadOnlyList<string> Aliases, string FolderPath, Guid NavigationFolderId, int SortOrder, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? LastUsedAtUtc)` with `DisplayTitle` and normalized alias matching.
- Produces: `ConfigurationMigrator.FromLegacy(AppSettings, IReadOnlyList<FolderMapping>, TimeProvider)`.
- Produces: `AppConfiguration.NavigationFolders`, `Rules`, `PinnedFolderIds`, and mutation methods for folders, rules, and pins.
- Preserves: `AppConfiguration.Mappings`, `FindMapping`, `FindMappings`, `AddMapping`, `RemoveMapping`, and `MarkMappingUsed` as derived compatibility APIs.

- [x] **Step 1: Write migration and invariant tests**

Add tests that prove old mappings group by target path, metadata aggregates correctly, and multi-target keywords survive:

```csharp
[Fact]
public void FromLegacy_GroupsAliasesByPathWithoutChangingKeywordTargets()
{
    var created = new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero);
    var configuration = ConfigurationMigrator.FromLegacy(
        new AppSettings { SchemaVersion = 1 },
        [
            new FolderMapping("docs", @"C:\Work\Docs", created, created, null),
            new FolderMapping("manual", @"c:\work\docs", created.AddMinutes(1), created.AddMinutes(2), null),
            new FolderMapping("docs", @"D:\Archive\Docs", created, created, null)
        ],
        new FixedTimeProvider(created.AddDays(1)));

    Assert.Equal(2, configuration.Rules.Count);
    Assert.Equal(2, configuration.FindMappings("docs").Count);
    Assert.Equal(["docs", "manual"], configuration.Rules.Single(rule => rule.FolderPath.StartsWith("C:")).Aliases);
    Assert.All(configuration.Rules, rule => Assert.Equal(configuration.UncategorizedFolderId, rule.NavigationFolderId));
}
```

Add model tests for same-parent name rejection, cross-parent duplicate names, cycle rejection, ordered pins, duplicate-path alias merging, cloning, and derived `Mappings` read-only behavior.

- [x] **Step 2: Run the new tests and verify RED**

Run:

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --filter "ConfigurationMigratorTests|AppConfigurationTests|JsonMappingStoreTests"
```

Expected: compilation fails because `ConfigurationMigrator`, `NavigationFolder`, `FolderRule`, and schema-v2 configuration APIs do not exist.

- [x] **Step 3: Implement the schema-v2 models and mutation rules**

Use immutable records for stored nodes and keep mutation in `AppConfiguration`:

```csharp
public sealed record NavigationFolder(
    Guid Id,
    Guid? ParentId,
    string Name,
    int SortOrder,
    DateTimeOffset UpdatedAtUtc);

public sealed record FolderRule(
    Guid Id,
    string Title,
    IReadOnlyList<string> Aliases,
    string FolderPath,
    Guid NavigationFolderId,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastUsedAtUtc)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        : Title.Trim();
}
```

`AppConfiguration` must expose exact operations:

```csharp
NavigationFolder AddNavigationFolder(string name, Guid? parentId);
NavigationFolder RenameNavigationFolder(Guid folderId, string name);
NavigationFolder MoveNavigationFolder(Guid folderId, Guid? parentId, int sortOrder);
FolderRule AddRule(string title, IEnumerable<string> aliases, string folderPath, Guid folderId);
FolderRule UpdateRule(Guid ruleId, string title, IEnumerable<string> aliases, string folderPath, Guid folderId);
bool RemoveRule(Guid ruleId);
void PinFolder(Guid folderId);
void UnpinFolder(Guid folderId);
void ReorderPinnedFolder(Guid folderId, int targetIndex);
```

Create an undeletable `未分类` folder for new and migrated configurations. Reject blank names, same-parent names, missing parent IDs, cycles, missing category references, blank aliases/paths, and duplicate physical paths.

- [x] **Step 4: Implement version-aware JSON load and schema-v2 save**

Use a private legacy DTO in `JsonMappingStore` so `Mappings` can be `[JsonIgnore]` in schema v2. When JSON has no `rules` array or `SchemaVersion < 2`, deserialize legacy mappings and call `ConfigurationMigrator.FromLegacy`; otherwise deserialize `AppConfiguration`. Save only `settings`, `navigationFolders`, `rules`, and `pinnedFolderIds`, preserving the existing temporary-file plus atomic-replace sequence.

- [x] **Step 5: Run focused and full core tests**

Run:

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --filter "ConfigurationMigratorTests|AppConfigurationTests|JsonMappingStoreTests"
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore
```

Expected: all migration/configuration tests pass, followed by all core tests passing with zero failures.

- [x] **Step 6: Commit schema v2**

```bash
git add src/QuickSearch.Core/NavigationFolder.cs src/QuickSearch.Core/FolderRule.cs src/QuickSearch.Core/ConfigurationMigrator.cs src/QuickSearch.Core/AppSettings.cs src/QuickSearch.Core/AppConfiguration.cs src/QuickSearch.Core/JsonMappingStore.cs tests/QuickSearch.Core.Tests/ConfigurationMigratorTests.cs tests/QuickSearch.Core.Tests/AppConfigurationTests.cs tests/QuickSearch.Core.Tests/JsonMappingStoreTests.cs
git commit -m "feat: add hierarchical rule catalog"
```

### Task 2: Hierarchical explorer state, pins, and local search

**Files:**
- Modify: `src/QuickSearch.Core/ViewModelCommands.cs`
- Create: `src/QuickSearch.Core/NavigationFolderNodeViewModel.cs`
- Create: `src/QuickSearch.Core/ExplorerSearchResult.cs`
- Create: `src/QuickSearch.Core/RuleExplorerViewModel.cs`
- Create: `tests/QuickSearch.Core.Tests/RuleExplorerViewModelTests.cs`

**Interfaces:**
- Consumes: schema-v2 `AppConfiguration`, `IMappingStore`, and `IFolderOpener`.
- Produces: parameterized `RelayCommand<T>` and `AsyncRelayCommand<T>` for WPF command parameters.
- Produces: `RuleExplorerViewModel.RootFolders`, `PinnedFolders`, `CurrentFolder`, `Breadcrumbs`, `ChildFolders`, `CurrentRules`, `SearchResults`, and `IsSearching`.
- Produces: navigation, pin/unpin, pin reorder, create-child, rename, open-rule, local-search, and clear-search commands.

- [x] **Step 1: Write failing explorer behavior tests**

Create tests using real `AppConfiguration` and in-memory store/opener doubles. Each test names a user-visible break:

```csharp
[Fact]
public void SearchText_MatchesRuleAliasAndRestoresPreviousFolderWhenCleared()
{
    var configuration = ExplorerFixtures.CreateConfiguration();
    var store = new RecordingMappingStore(configuration);
    var viewModel = new RuleExplorerViewModel(configuration, store, new RecordingFolderOpener());
    viewModel.NavigateToFolder(configuration.NavigationFolders.Single(folder => folder.Name == "开发").Id);

    viewModel.SearchText = "fastapi";

    var result = Assert.Single(viewModel.SearchResults);
    Assert.Equal(ExplorerSearchResultKind.Rule, result.Kind);
    Assert.Contains("客户 › 小溪", result.CategoryPath);

    viewModel.SearchText = string.Empty;
    Assert.Equal("开发", viewModel.CurrentFolder.Name);
}
```

Also test folder search by full category path, pin persistence, pin save rollback, pin reorder, child creation, same-parent rename errors, rule single-click open, invalid-path status, and status events.

- [x] **Step 2: Run explorer tests and verify RED**

Run:

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --filter RuleExplorerViewModelTests
```

Expected: compilation fails because explorer view models and generic commands do not exist.

- [x] **Step 3: Implement parameterized commands and explorer projections**

Implement generic commands that validate parameter type, then build tree nodes recursively from folder IDs. `RuleExplorerViewModel` filters local in-memory data synchronously, preserves the pre-search folder ID, produces separate folder/rule result kinds, and computes breadcrumb/category paths without storing UI expansion state in configuration. The WPF search binding applies `Delay=150`, so production code needs no test-only wait method.

- [x] **Step 4: Implement atomic lightweight management and rule opening**

Every immediate edit follows this exact sequence:

```csharp
var before = configuration.Clone();
var candidate = configuration.Clone();
apply(candidate);
try
{
    await store.SaveAsync(candidate);
    configuration.ReplaceWith(candidate);
    RefreshFromConfiguration();
}
catch
{
    configuration.ReplaceWith(before);
    RefreshFromConfiguration();
    throw;
}
```

Opening a rule validates the target with an injected `Func<string, bool>`, calls `IFolderOpener.Open`, updates the matching rule usage timestamp through `AppConfiguration.MarkMappingUsed`, persists it, and reports success/failure without creating new rules.

- [x] **Step 5: Run explorer and full tests**

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --filter RuleExplorerViewModelTests
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore
```

Expected: explorer tests and the complete core suite pass.

- [x] **Step 6: Commit explorer state**

```bash
git add src/QuickSearch.Core/ViewModelCommands.cs src/QuickSearch.Core/NavigationFolderNodeViewModel.cs src/QuickSearch.Core/ExplorerSearchResult.cs src/QuickSearch.Core/RuleExplorerViewModel.cs tests/QuickSearch.Core.Tests/RuleExplorerViewModelTests.cs
git commit -m "feat: add hierarchical rule explorer state"
```

### Task 3: Launcher integration and Everything fallback

**Files:**
- Modify: `src/QuickSearch.Core/LauncherViewModel.cs`
- Modify: `tests/QuickSearch.Core.Tests/LauncherViewModelTests.cs`

**Interfaces:**
- Consumes: `RuleExplorerViewModel` and schema-v2 rule lookup.
- Produces: `LauncherViewModel.Explorer`, `IsEverythingSearchVisible`, `CanSearchEverything`, and `SearchEverythingCommand`.
- Preserves: clipboard activation disposition, multi-folder opening, result selection, cancellation, and existing status semantics.

- [x] **Step 1: Write failing launcher integration tests**

Add tests proving that initialization populates explorer state, clipboard hits use schema-v2 rules, local search does not call Everything, and local misses expose the explicit Everything fallback:

```csharp
[Fact]
public async Task LocalSearchMiss_ExposesEverythingFallbackWithoutCallingSearch()
{
    var search = new RecordingFolderSearch();
    var viewModel = LauncherFixtures.Create(search: search);
    await viewModel.InitializeAsync();

    viewModel.Explorer.SearchText = "not-in-rules";

    Assert.True(viewModel.CanSearchEverything);
    Assert.Empty(search.Queries);
}
```

Add a test that invoking the fallback fills existing `Results`, preserves arrow/Enter selection, and leaves the local search text intact.

- [x] **Step 2: Run launcher tests and verify RED**

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --filter LauncherViewModelTests
```

Expected: compilation fails because the explorer and fallback properties are absent.

- [x] **Step 3: Compose explorer state into LauncherViewModel**

Construct `RuleExplorerViewModel` after configuration load, forward explorer status events through existing `SetStatus`, and make `RefreshConfiguration` rebuild explorer projections. Keep `ActivateFromClipboardAsync` querying `Configuration.FindMappings(keyword)` so multi-target opening behavior remains stable.

- [x] **Step 4: Separate explicit Everything fallback from local search**

Local text changes update explorer results only. `SearchEverythingCommand` copies trimmed explorer search text into `FolderQuery`, calls existing `SearchNowAsync`, and exposes the existing result list. Clearing explorer search clears Everything results and returns to the prior category.

- [x] **Step 5: Run launcher and full tests**

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --filter LauncherViewModelTests
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore
```

Expected: launcher integration and all core tests pass.

- [x] **Step 6: Commit launcher integration**

```bash
git add src/QuickSearch.Core/LauncherViewModel.cs tests/QuickSearch.Core.Tests/LauncherViewModelTests.cs
git commit -m "feat: integrate explorer with quick search"
```

### Task 4: Transactional category and rule organizer

**Files:**
- Create: `src/QuickSearch.Core/FolderRuleEditorViewModel.cs`
- Create: `src/QuickSearch.Core/NavigationOrganizerViewModel.cs`
- Modify: `src/QuickSearch.Core/SettingsViewModel.cs`
- Create: `tests/QuickSearch.Core.Tests/NavigationOrganizerViewModelTests.cs`
- Modify: `tests/QuickSearch.Core.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: schema-v2 configuration clone and existing settings transaction.
- Produces: staged tree nodes, selected folder, editable current-folder rules, new/rename/move/delete folder operations, new/edit/delete/batch-move rules, and deletion impact counts.
- Preserves: shortcut/startup rollback and `Saved`/`HideRequested` event behavior.

- [x] **Step 1: Write failing organizer tests**

Cover staged edits, cancel, save, subtree moves, cycle rejection, batch rule moves, and both non-empty deletion choices:

```csharp
[Fact]
public void DeleteFolderAndMoveContents_ReparentsChildrenWithoutFlatteningGrandchildren()
{
    var organizer = OrganizerFixtures.Create();
    var customer = organizer.FindFolder("客户");
    var project = organizer.FindFolder("项目");
    var archive = organizer.FindFolder("归档");

    organizer.DeleteFolder(customer.Id, FolderDeletionMode.MoveContentsToParent);

    Assert.Equal(organizer.UncategorizedFolderId, organizer.FindFolder("项目").ParentId);
    Assert.Equal(project.Id, organizer.FindFolder("归档").ParentId);
    Assert.DoesNotContain(organizer.Folders, folder => folder.Id == customer.Id);
}
```

- [x] **Step 2: Run organizer/settings tests and verify RED**

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --filter "NavigationOrganizerViewModelTests|SettingsViewModelTests"
```

Expected: compilation fails because organizer types and folder deletion mode do not exist.

- [x] **Step 3: Implement staged organizer models**

`NavigationOrganizerViewModel` owns a clone created by `BeginEdit`, never the live configuration. Implement `FolderDeletionMode.MoveContentsToParent` by moving direct rules and direct child folders only; descendants remain attached to their direct parents. The undeletable `未分类` node receives content when deleting a top-level folder.

- [x] **Step 4: Integrate organizer candidate into SettingsViewModel transaction**

Replace grouped-mapping candidate construction with `Organizer.BuildCandidate(shortcut, startWithWindows)`. Keep the existing order: validate candidate, replace hotkey, replace startup registration, atomically save, then replace live configuration. Preserve rollback of all attempted external side effects.

- [x] **Step 5: Run organizer/settings and full tests**

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --filter "NavigationOrganizerViewModelTests|SettingsViewModelTests"
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore
```

Expected: organizer/settings tests and all core tests pass.

- [x] **Step 6: Commit organizer state**

```bash
git add src/QuickSearch.Core/FolderRuleEditorViewModel.cs src/QuickSearch.Core/NavigationOrganizerViewModel.cs src/QuickSearch.Core/SettingsViewModel.cs tests/QuickSearch.Core.Tests/NavigationOrganizerViewModelTests.cs tests/QuickSearch.Core.Tests/SettingsViewModelTests.cs
git commit -m "feat: manage hierarchical rules in settings"
```

### Task 5: Tree explorer WPF interface and drag interactions

**Files:**
- Modify: `src/QuickSearch.Windows/MainWindow.xaml`
- Modify: `src/QuickSearch.Windows/MainWindow.xaml.cs`
- Modify: `src/QuickSearch.Windows/SettingsWindow.xaml`
- Modify: `src/QuickSearch.Windows/SettingsWindow.xaml.cs`
- Modify: `src/QuickSearch.Windows/Themes/ModernTheme.xaml`
- Modify: `src/QuickSearch.Windows/App.xaml.cs`

**Interfaces:**
- Consumes: launcher explorer/fallback state and settings organizer state.
- Produces: top pinned `ItemsControl`, left hierarchical `TreeView`, breadcrumb/search header, child-folder cards, rule rows, Everything result state, bottom-right settings gear, and settings organizer panes.
- Code-behind boundary: WPF pointer/focus/drag event translation only; all validation and mutation remain in view models.

- [ ] **Step 1: Add view-model characterization tests for every XAML command**

Before XAML changes, add or extend tests so every command referenced by XAML has an observable result. Use tests with this shape for navigation and command parameters:

```csharp
[Fact]
public void NavigateFolderCommand_UsesFolderParameterAndRefreshesVisibleContent()
{
    var configuration = ExplorerFixtures.CreateConfiguration();
    var viewModel = new RuleExplorerViewModel(
        configuration,
        new RecordingMappingStore(configuration),
        new RecordingFolderOpener());
    var target = configuration.NavigationFolders.Single(folder => folder.Name == "开发");

    viewModel.NavigateFolderCommand.Execute(target);

    Assert.Equal(target.Id, viewModel.CurrentFolder.Id);
    Assert.All(viewModel.CurrentRules, rule => Assert.Equal(target.Id, rule.NavigationFolderId));
}
```

Add equivalently focused tests for open rule, pin, reorder pin, create/rename folder, local search, Everything fallback, open settings, save organizer, and cancel organizer. Each assertion must target configuration, navigation state, opened path, or emitted event rather than the command object itself. Run the focused tests and verify RED for every command not yet implemented.

- [x] **Step 2: Replace MainWindow with the confirmed explorer layout**

Use these row/column boundaries:

```xml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
    <RowDefinition Height="Auto" />
  </Grid.RowDefinitions>
  <ScrollViewer Grid.Row="0" HorizontalScrollBarVisibility="Auto">
    <ItemsControl ItemsSource="{Binding Explorer.PinnedFolders}" />
  </ScrollViewer>
  <Grid Grid.Row="1">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="236" />
      <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>
    <TreeView ItemsSource="{Binding Explorer.RootFolders}" />
    <Grid Grid.Column="1">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
      </Grid.RowDefinitions>
      <Grid>
        <ItemsControl ItemsSource="{Binding Explorer.Breadcrumbs}" />
        <TextBox HorizontalAlignment="Right"
                 Text="{Binding Explorer.SearchText, UpdateSourceTrigger=PropertyChanged, Delay=150}" />
      </Grid>
      <ItemsControl Grid.Row="1" ItemsSource="{Binding Explorer.ChildFolders}" />
      <ItemsControl Grid.Row="2" ItemsSource="{Binding Explorer.CurrentRules}" />
    </Grid>
  </Grid>
  <Grid Grid.Row="2">
    <TextBlock Text="{Binding Status}" />
    <Button HorizontalAlignment="Right"
            Content="⚙"
            Command="{Binding ShowSettingsCommand}" />
  </Grid>
</Grid>
```

Set the window `Title=""`; remove the in-content title, clipboard copy, alias/form cards, and large outer decorative card. Bind folder cards to navigation commands, rule rows to single-click open commands, and Everything results to existing selection/open commands.

- [x] **Step 3: Implement home drag/reorder and context menus**

Translate pin drag start/drop to `Explorer.ReorderPinnedFolderCommand` with source ID and target index. Folder context menus expose new child, rename, pin, and unpin only. Code-behind must not mutate configuration collections directly.

- [x] **Step 4: Replace settings mapping list with organizer panes**

Keep general settings as the first tab and add a “分类与规则” tab: left tree plus folder actions, right current-folder rule grid plus new/delete/batch-move actions. Bind Save/Cancel to the existing settings transaction and keep destructive deletion behind an explicit dialog that passes the selected `FolderDeletionMode`.

- [x] **Step 5: Add focused theme resources**

Add `PinnedFolderButtonStyle`, `ExplorerTreeItemStyle`, `FolderCardButtonStyle`, `RuleRowButtonStyle`, `BottomGearButtonStyle`, `BreadcrumbButtonStyle`, and organizer tree/data-grid styles derived from the existing Path Beam tokens. Use 120–160 ms color/opacity transitions only; retain visible keyboard focus and reduced-motion compatibility.

- [ ] **Step 6: Build on available platforms**

Run:

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore
dotnet build src/QuickSearch.Core/QuickSearch.Core.csproj --no-restore -warnaserror
```

Then trigger or use the Windows environment for:

```powershell
dotnet build src/QuickSearch.Windows/QuickSearch.Windows.csproj -c Release -warnaserror
```

Expected: core tests pass, core build has zero warnings, and Windows WPF Release build has zero warnings/errors.

- [x] **Step 7: Commit the WPF interface**

```bash
git add src/QuickSearch.Windows/MainWindow.xaml src/QuickSearch.Windows/MainWindow.xaml.cs src/QuickSearch.Windows/SettingsWindow.xaml src/QuickSearch.Windows/SettingsWindow.xaml.cs src/QuickSearch.Windows/Themes/ModernTheme.xaml src/QuickSearch.Windows/App.xaml.cs
git commit -m "feat(ui): add tree rule explorer"
```

### Task 6: Regression, migration fixture, and delivery verification

**Files:**
- Verify: `docs/superpowers/specs/2026-08-12-hierarchical-rule-explorer-design.md`
- Verify: `.github/workflows/windows-release.yml`

No source modification is planned in this task. A regression failure returns execution to the task that owns the failing interface, adds a focused failing test there, and follows RED-GREEN-REFACTOR before this verification task resumes.

**Interfaces:**
- Consumes: completed schema, explorer, launcher, organizer, and WPF UI.
- Produces: evidence that old configuration migrates, all core behavior remains green, Windows build/package succeeds, and unrelated worktree changes remain untouched.

- [x] **Step 1: Run complete core regression**

```bash
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj --no-restore --logger "console;verbosity=normal"
```

Expected: zero failed tests and no test-host errors.

- [x] **Step 2: Run configuration migration smoke test**

Use a temporary version-1 JSON fixture containing multiple aliases for one path and one alias for multiple paths. Load it through `JsonMappingStore`, save it, reload it, and assert with the automated test that keyword targets, timestamps, `未分类`, and schema version 2 survive the round trip.

- [x] **Step 3: Run formatting and scope checks**

```bash
dotnet format QuickSearch.sln --no-restore --verify-no-changes
git diff --check
git status --short --branch
```

Expected: formatting and whitespace checks pass; unrelated pre-existing changes remain present but unstaged by feature commits.

- [ ] **Step 4: Verify Windows build/package**

Run the existing Windows release workflow for the current branch or reproduce its exact Release commands on Windows. Verify the produced package launches and manually check: pin reorder, deep tree navigation, single-click rule opening, `Ctrl+K`, Everything fallback, settings save/cancel, and global shortcut multi-open.

- [x] **Step 5: Commit the implementation plan and any final tested correction**

```bash
git add docs/superpowers/plans/2026-08-12-hierarchical-rule-explorer-implementation.md
git commit -m "docs: plan hierarchical rule explorer"
```
