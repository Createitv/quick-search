using System.Text.Json.Serialization;

namespace QuickSearch.Core;

public sealed class AppConfiguration
{
    public static readonly Guid DefaultUncategorizedFolderId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly List<NavigationFolder> _navigationFolders = [];
    private readonly List<FolderRule> _rules = [];
    private readonly List<Guid> _pinnedFolderIds = [];
    private TimeProvider _timeProvider;

    public AppConfiguration()
        : this(TimeProvider.System)
    {
    }

    public AppConfiguration(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        EnsureUncategorizedFolder();
    }

    public AppSettings Settings { get; set; } = new();

    [JsonIgnore]
    public Guid UncategorizedFolderId =>
        _navigationFolders.First(folder =>
            folder.Id == DefaultUncategorizedFolderId).Id;

    public IReadOnlyList<NavigationFolder> NavigationFolders
    {
        get => _navigationFolders.AsReadOnly();
        init
        {
            _navigationFolders.Clear();
            _navigationFolders.AddRange((value ?? [])
                .Select(folder => folder with { Name = folder.Name.Trim() }));
            EnsureUncategorizedFolder();
            ValidateFolders();
        }
    }

    public IReadOnlyList<FolderRule> Rules
    {
        get => _rules.AsReadOnly();
        init
        {
            _rules.Clear();
            foreach (var rule in value ?? [])
            {
                AddStoredRule(rule);
            }
        }
    }

    public IReadOnlyList<Guid> PinnedFolderIds
    {
        get => _pinnedFolderIds.AsReadOnly();
        init
        {
            _pinnedFolderIds.Clear();
            foreach (var folderId in value ?? [])
            {
                if (_navigationFolders.Any(folder => folder.Id == folderId)
                    && !_pinnedFolderIds.Contains(folderId))
                {
                    _pinnedFolderIds.Add(folderId);
                }
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<FolderMapping> Mappings
    {
        get => BuildMappings().AsReadOnly();
        init => ImportLegacyMappings(value ?? []);
    }

    public NavigationFolder AddNavigationFolder(string name, Guid? parentId)
    {
        var normalizedName = ValidateFolderName(name, parentId, excludedId: null);
        ValidateParentExists(parentId);
        var folder = new NavigationFolder(
            Guid.NewGuid(),
            parentId,
            normalizedName,
            GetNextFolderSortOrder(parentId),
            UtcNow());
        _navigationFolders.Add(folder);
        return folder;
    }

    public NavigationFolder RenameNavigationFolder(Guid folderId, string name)
    {
        var index = GetFolderIndex(folderId);
        var existing = _navigationFolders[index];
        var normalizedName = ValidateFolderName(name, existing.ParentId, folderId);
        var updated = existing with
        {
            Name = normalizedName,
            UpdatedAtUtc = UtcNow()
        };
        _navigationFolders[index] = updated;
        return updated;
    }

    public NavigationFolder MoveNavigationFolder(
        Guid folderId,
        Guid? parentId,
        int sortOrder)
    {
        if (folderId == UncategorizedFolderId)
        {
            throw new InvalidOperationException("未分类文件夹不能移动。");
        }

        ValidateParentExists(parentId);
        if (parentId == folderId || IsDescendant(parentId, folderId))
        {
            throw new InvalidOperationException("文件夹不能移动到自身或自己的子文件夹中。");
        }

        var index = GetFolderIndex(folderId);
        var existing = _navigationFolders[index];
        ValidateFolderName(existing.Name, parentId, folderId);
        var updated = existing with
        {
            ParentId = parentId,
            SortOrder = Math.Max(0, sortOrder),
            UpdatedAtUtc = UtcNow()
        };
        _navigationFolders[index] = updated;
        NormalizeFolderOrder(existing.ParentId);
        NormalizeFolderOrder(parentId);
        return _navigationFolders[GetFolderIndex(folderId)];
    }

    public NavigationFolder MoveNavigationFolderRelative(
        Guid folderId,
        Guid targetFolderId,
        bool placeAfterTarget)
    {
        if (folderId == UncategorizedFolderId)
        {
            throw new InvalidOperationException("未分类文件夹不能移动。");
        }

        if (folderId == targetFolderId)
        {
            return _navigationFolders[GetFolderIndex(folderId)];
        }

        var sourceIndex = GetFolderIndex(folderId);
        var source = _navigationFolders[sourceIndex];
        var target = _navigationFolders[GetFolderIndex(targetFolderId)];
        var parentId = target.ParentId;
        ValidateParentExists(parentId);
        if (parentId == folderId || IsDescendant(parentId, folderId))
        {
            throw new InvalidOperationException("文件夹不能移动到自身或自己的子文件夹中。");
        }

        ValidateFolderName(source.Name, parentId, folderId);
        var updated = source with
        {
            ParentId = parentId,
            UpdatedAtUtc = UtcNow()
        };
        var siblings = _navigationFolders
            .Where(folder => folder.ParentId == parentId && folder.Id != folderId)
            .OrderBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var relativeIndex = siblings.FindIndex(folder => folder.Id == targetFolderId);
        if (relativeIndex < 0)
        {
            throw new InvalidOperationException("目标文件夹不存在。");
        }

        siblings.Insert(placeAfterTarget ? relativeIndex + 1 : relativeIndex, updated);
        for (var order = 0; order < siblings.Count; order++)
        {
            var index = GetFolderIndex(siblings[order].Id);
            _navigationFolders[index] = siblings[order] with { SortOrder = order };
        }

        if (source.ParentId != parentId)
        {
            NormalizeFolderOrder(source.ParentId);
        }

        return _navigationFolders[GetFolderIndex(folderId)];
    }

    public bool RemoveNavigationFolder(Guid folderId)
    {
        if (folderId == UncategorizedFolderId)
        {
            return false;
        }

        if (_navigationFolders.Any(folder => folder.ParentId == folderId)
            || _rules.Any(rule => rule.NavigationFolderId == folderId))
        {
            throw new InvalidOperationException("非空文件夹不能直接删除。");
        }

        var removed = _navigationFolders.RemoveAll(folder => folder.Id == folderId) > 0;
        if (removed)
        {
            _pinnedFolderIds.Remove(folderId);
        }

        return removed;
    }

    public FolderRule AddRule(
        string title,
        IEnumerable<string> aliases,
        string folderPath,
        Guid folderId)
    {
        ValidateFolderExists(folderId);
        var path = ValidatePath(folderPath);
        var normalizedAliases = NormalizeAliases(aliases);
        var existingIndex = FindRuleIndexByPath(path);
        if (existingIndex >= 0)
        {
            var existing = _rules[existingIndex];
            var mergedAliases = NormalizeAliases(existing.Aliases.Concat(normalizedAliases));
            var updated = existing with
            {
                Title = string.IsNullOrWhiteSpace(existing.Title)
                    ? title?.Trim() ?? string.Empty
                    : existing.Title,
                Aliases = mergedAliases,
                UpdatedAtUtc = UtcNow()
            };
            _rules[existingIndex] = updated;
            return updated;
        }

        var utcNow = UtcNow();
        var rule = new FolderRule(
            Guid.NewGuid(),
            title?.Trim() ?? string.Empty,
            normalizedAliases,
            path,
            folderId,
            GetNextRuleSortOrder(folderId),
            utcNow,
            utcNow,
            null);
        _rules.Add(rule);
        return rule;
    }

    public FolderRule UpdateRule(
        Guid ruleId,
        string title,
        IEnumerable<string> aliases,
        string folderPath,
        Guid folderId)
    {
        ValidateFolderExists(folderId);
        var index = GetRuleIndex(ruleId);
        var path = ValidatePath(folderPath);
        var conflictingIndex = FindRuleIndexByPath(path);
        if (conflictingIndex >= 0 && conflictingIndex != index)
        {
            throw new InvalidOperationException($"文件夹路径已存在规则：{path}");
        }

        var existing = _rules[index];
        var updated = existing with
        {
            Title = title?.Trim() ?? string.Empty,
            Aliases = NormalizeAliases(aliases),
            FolderPath = path,
            NavigationFolderId = folderId,
            UpdatedAtUtc = UtcNow()
        };
        _rules[index] = updated;
        return updated;
    }

    public bool RemoveRule(Guid ruleId) =>
        _rules.RemoveAll(rule => rule.Id == ruleId) > 0;

    public FolderRule MoveRule(Guid ruleId, Guid targetFolderId)
    {
        ValidateFolderExists(targetFolderId);
        var index = GetRuleIndex(ruleId);
        var existing = _rules[index];
        if (existing.NavigationFolderId == targetFolderId)
        {
            return existing;
        }

        var sourceFolderId = existing.NavigationFolderId;
        var moved = existing with
        {
            NavigationFolderId = targetFolderId,
            SortOrder = GetNextRuleSortOrder(targetFolderId),
            UpdatedAtUtc = UtcNow()
        };
        _rules[index] = moved;
        NormalizeRuleOrder(sourceFolderId);
        return moved;
    }

    public FolderRule ReorderRuleRelative(
        Guid ruleId,
        Guid targetRuleId,
        bool placeAfterTarget)
    {
        var sourceIndex = GetRuleIndex(ruleId);
        var source = _rules[sourceIndex];
        var target = _rules[GetRuleIndex(targetRuleId)];
        if (source.Id == target.Id)
        {
            return source;
        }

        var targetFolderId = target.NavigationFolderId;
        var updated = source with
        {
            NavigationFolderId = targetFolderId,
            UpdatedAtUtc = UtcNow()
        };
        var siblings = _rules
            .Where(rule => rule.NavigationFolderId == targetFolderId && rule.Id != ruleId)
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var relativeIndex = siblings.FindIndex(rule => rule.Id == targetRuleId);
        if (relativeIndex < 0)
        {
            throw new InvalidOperationException("目标快捷导航不存在。");
        }

        siblings.Insert(placeAfterTarget ? relativeIndex + 1 : relativeIndex, updated);
        for (var order = 0; order < siblings.Count; order++)
        {
            var index = GetRuleIndex(siblings[order].Id);
            _rules[index] = siblings[order] with { SortOrder = order };
        }

        if (source.NavigationFolderId != targetFolderId)
        {
            NormalizeRuleOrder(source.NavigationFolderId);
        }

        return _rules[GetRuleIndex(ruleId)];
    }

    public void PinFolder(Guid folderId)
    {
        ValidateFolderExists(folderId);
        if (!_pinnedFolderIds.Contains(folderId))
        {
            _pinnedFolderIds.Add(folderId);
        }
    }

    public void UnpinFolder(Guid folderId) => _pinnedFolderIds.Remove(folderId);

    public void ReorderPinnedFolder(Guid folderId, int targetIndex)
    {
        var sourceIndex = _pinnedFolderIds.IndexOf(folderId);
        if (sourceIndex < 0)
        {
            throw new InvalidOperationException("只能排序已收藏的文件夹。");
        }

        _pinnedFolderIds.RemoveAt(sourceIndex);
        _pinnedFolderIds.Insert(
            Math.Clamp(targetIndex, 0, _pinnedFolderIds.Count),
            folderId);
    }

    public void ReorderPinnedFolderRelative(
        Guid folderId,
        Guid targetFolderId,
        bool placeAfterTarget)
    {
        var sourceIndex = _pinnedFolderIds.IndexOf(folderId);
        var targetIndex = _pinnedFolderIds.IndexOf(targetFolderId);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            throw new InvalidOperationException("只能排序已收藏的文件夹。");
        }

        if (folderId == targetFolderId)
        {
            return;
        }

        _pinnedFolderIds.RemoveAt(sourceIndex);
        targetIndex = _pinnedFolderIds.IndexOf(targetFolderId);
        _pinnedFolderIds.Insert(
            placeAfterTarget ? targetIndex + 1 : targetIndex,
            folderId);
    }

    public FolderMapping? FindMapping(string alias) =>
        FindMappings(alias).FirstOrDefault();

    public IReadOnlyList<FolderMapping> FindMappings(string alias)
    {
        var normalizedAlias = AliasNormalizer.Normalize(alias);
        return _rules
            .Where(rule => rule.Aliases.Any(candidate => string.Equals(
                AliasNormalizer.Normalize(candidate),
                normalizedAlias,
                StringComparison.Ordinal)))
            .OrderBy(rule => rule.SortOrder)
            .Select(rule => CreateMapping(
                rule,
                rule.Aliases.First(candidate => string.Equals(
                    AliasNormalizer.Normalize(candidate),
                    normalizedAlias,
                    StringComparison.Ordinal))))
            .ToArray();
    }

    public FolderMapping AddMapping(string alias, string folderPath)
    {
        var path = ValidatePath(folderPath);
        var existing = FindMappings(alias).FirstOrDefault(mapping =>
            string.Equals(
                mapping.FolderPath,
                path,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var rule = AddRule(string.Empty, [alias], path, UncategorizedFolderId);
        return CreateMapping(rule, alias.Trim());
    }

    public FolderMapping UpsertMapping(string alias, string folderPath)
    {
        var existing = FindMapping(alias);
        if (existing is null)
        {
            return AddMapping(alias, folderPath);
        }

        return UpdateMapping(existing.Alias, existing.FolderPath, alias, folderPath);
    }

    public FolderMapping? MarkMappingUsed(string alias)
    {
        var mapping = FindMapping(alias);
        return mapping is null
            ? null
            : MarkMappingUsed(mapping.Alias, mapping.FolderPath);
    }

    public FolderMapping? MarkMappingUsed(string alias, string folderPath)
    {
        var index = FindRuleIndexByPath(folderPath);
        if (index < 0 || !_rules[index].MatchesAlias(alias))
        {
            return null;
        }

        var updated = _rules[index] with { LastUsedAtUtc = UtcNow() };
        _rules[index] = updated;
        var storedAlias = updated.Aliases.First(candidate => string.Equals(
            AliasNormalizer.Normalize(candidate),
            AliasNormalizer.Normalize(alias),
            StringComparison.Ordinal));
        return CreateMapping(updated, storedAlias);
    }

    public bool RemoveMapping(string alias)
    {
        var removed = false;
        foreach (var rule in _rules.ToArray())
        {
            if (RemoveAliasFromRule(rule.Id, alias))
            {
                removed = true;
            }
        }

        return removed;
    }

    public bool RemoveMapping(string alias, string folderPath)
    {
        var index = FindRuleIndexByPath(folderPath);
        return index >= 0 && RemoveAliasFromRule(_rules[index].Id, alias);
    }

    public FolderMapping UpdateMapping(
        string originalAlias,
        string alias,
        string folderPath)
    {
        var original = FindMapping(originalAlias);
        return original is null
            ? UpsertMapping(alias, folderPath)
            : UpdateMapping(originalAlias, original.FolderPath, alias, folderPath);
    }

    public FolderMapping UpdateMapping(
        string originalAlias,
        string originalFolderPath,
        string alias,
        string folderPath)
    {
        var originalIndex = FindRuleIndexByPath(originalFolderPath);
        if (originalIndex < 0 || !_rules[originalIndex].MatchesAlias(originalAlias))
        {
            return AddMapping(alias, folderPath);
        }

        var originalRule = _rules[originalIndex];
        var createdAt = originalRule.CreatedAtUtc;
        var lastUsed = originalRule.LastUsedAtUtc;
        RemoveAliasFromRule(originalRule.Id, originalAlias);
        var updated = AddMapping(alias, folderPath);
        var updatedIndex = FindRuleIndexByPath(updated.FolderPath);
        var updatedRule = _rules[updatedIndex] with
        {
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = UtcNow(),
            LastUsedAtUtc = lastUsed
        };
        _rules[updatedIndex] = updatedRule;
        return CreateMapping(updatedRule, alias.Trim());
    }

    public IReadOnlyList<FolderMapping> GetMappingsForPath(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        var index = FindRuleIndexByPath(folderPath);
        if (index < 0)
        {
            return [];
        }

        var rule = _rules[index];
        return rule.Aliases.Select(alias => CreateMapping(rule, alias)).ToArray();
    }

    public AppConfiguration Clone() => new(_timeProvider)
    {
        Settings = Settings with { },
        NavigationFolders = _navigationFolders.Select(folder => folder with { }).ToArray(),
        Rules = _rules.Select(CloneRule).ToArray(),
        PinnedFolderIds = _pinnedFolderIds.ToArray()
    };

    public void ReplaceWith(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _timeProvider = configuration._timeProvider;
        Settings = configuration.Settings with { };
        _navigationFolders.Clear();
        _navigationFolders.AddRange(configuration._navigationFolders.Select(folder => folder with { }));
        _rules.Clear();
        _rules.AddRange(configuration._rules.Select(CloneRule));
        _pinnedFolderIds.Clear();
        _pinnedFolderIds.AddRange(configuration._pinnedFolderIds);
        EnsureUncategorizedFolder();
    }

    private static FolderRule CloneRule(FolderRule rule) => rule with
    {
        Aliases = rule.Aliases.ToArray()
    };

    private void ImportLegacyMappings(IEnumerable<FolderMapping> mappings)
    {
        _rules.Clear();
        foreach (var group in mappings.GroupBy(
                     mapping => mapping.FolderPath.Trim(),
                     StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            AddStoredRule(new FolderRule(
                Guid.NewGuid(),
                string.Empty,
                NormalizeAliases(ordered.Select(mapping => mapping.Alias)),
                ordered[0].FolderPath.Trim(),
                UncategorizedFolderId,
                _rules.Count,
                ordered.Min(mapping => mapping.CreatedAtUtc),
                ordered.Max(mapping => mapping.UpdatedAtUtc),
                ordered.Max(mapping => mapping.LastUsedAtUtc)));
        }
    }

    private void AddStoredRule(FolderRule rule)
    {
        ValidateFolderExists(rule.NavigationFolderId);
        var path = ValidatePath(rule.FolderPath);
        var aliases = NormalizeAliases(rule.Aliases);
        if (FindRuleIndexByPath(path) >= 0)
        {
            throw new InvalidOperationException($"文件夹路径已存在规则：{path}");
        }

        _rules.Add(rule with
        {
            Title = rule.Title?.Trim() ?? string.Empty,
            Aliases = aliases,
            FolderPath = path
        });
    }

    private List<FolderMapping> BuildMappings() => _rules
        .OrderBy(rule => rule.SortOrder)
        .SelectMany(rule => rule.Aliases.Select(alias => CreateMapping(rule, alias)))
        .ToList();

    private static FolderMapping CreateMapping(FolderRule rule, string alias) => new(
        alias,
        rule.FolderPath,
        rule.CreatedAtUtc,
        rule.UpdatedAtUtc,
        rule.LastUsedAtUtc);

    private bool RemoveAliasFromRule(Guid ruleId, string alias)
    {
        var index = GetRuleIndex(ruleId);
        var normalized = AliasNormalizer.Normalize(alias);
        var aliases = _rules[index].Aliases
            .Where(candidate => !string.Equals(
                AliasNormalizer.Normalize(candidate),
                normalized,
                StringComparison.Ordinal))
            .ToArray();
        if (aliases.Length == _rules[index].Aliases.Count)
        {
            return false;
        }

        if (aliases.Length == 0)
        {
            _rules.RemoveAt(index);
        }
        else
        {
            _rules[index] = _rules[index] with
            {
                Aliases = aliases,
                UpdatedAtUtc = UtcNow()
            };
        }

        return true;
    }

    private void EnsureUncategorizedFolder()
    {
        if (_navigationFolders.Any(folder => folder.Id == DefaultUncategorizedFolderId))
        {
            return;
        }

        _navigationFolders.Insert(0, new NavigationFolder(
            DefaultUncategorizedFolderId,
            null,
            "未分类",
            0,
            UtcNow()));
    }

    private void ValidateFolders()
    {
        if (_navigationFolders.Select(folder => folder.Id).Distinct().Count()
            != _navigationFolders.Count)
        {
            throw new InvalidOperationException("分类文件夹 ID 不能重复。");
        }

        foreach (var folder in _navigationFolders)
        {
            ValidateParentExists(folder.ParentId);
            ValidateFolderName(folder.Name, folder.ParentId, folder.Id);
            if (folder.ParentId == folder.Id || IsDescendant(folder.ParentId, folder.Id))
            {
                throw new InvalidOperationException("分类文件夹结构包含循环引用。");
            }
        }
    }

    private string ValidateFolderName(string name, Guid? parentId, Guid? excludedId)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new InvalidOperationException("文件夹名称不能为空。");
        }

        if (_navigationFolders.Any(folder =>
                folder.Id != excludedId
                && folder.ParentId == parentId
                && string.Equals(folder.Name, value, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"同一层级已存在文件夹：{value}");
        }

        return value;
    }

    private void ValidateParentExists(Guid? parentId)
    {
        if (parentId is not null
            && _navigationFolders.All(folder => folder.Id != parentId.Value))
        {
            throw new InvalidOperationException("父文件夹不存在。");
        }
    }

    private void ValidateFolderExists(Guid folderId)
    {
        if (_navigationFolders.All(folder => folder.Id != folderId))
        {
            throw new InvalidOperationException("规则所属文件夹不存在。");
        }
    }

    private bool IsDescendant(Guid? candidateId, Guid ancestorId)
    {
        var currentId = candidateId;
        var visited = new HashSet<Guid>();
        while (currentId is not null && visited.Add(currentId.Value))
        {
            if (currentId.Value == ancestorId)
            {
                return true;
            }

            currentId = _navigationFolders.FirstOrDefault(folder =>
                folder.Id == currentId.Value)?.ParentId;
        }

        return false;
    }

    private int GetFolderIndex(Guid folderId)
    {
        var index = _navigationFolders.FindIndex(folder => folder.Id == folderId);
        return index >= 0
            ? index
            : throw new InvalidOperationException("文件夹不存在。");
    }

    private int GetRuleIndex(Guid ruleId)
    {
        var index = _rules.FindIndex(rule => rule.Id == ruleId);
        return index >= 0
            ? index
            : throw new InvalidOperationException("规则不存在。");
    }

    private int FindRuleIndexByPath(string folderPath) => _rules.FindIndex(rule =>
        string.Equals(
            rule.FolderPath.Trim(),
            folderPath.Trim(),
            StringComparison.OrdinalIgnoreCase));

    private int GetNextFolderSortOrder(Guid? parentId) =>
        _navigationFolders
            .Where(folder => folder.ParentId == parentId)
            .Select(folder => folder.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;

    private int GetNextRuleSortOrder(Guid folderId) =>
        _rules
            .Where(rule => rule.NavigationFolderId == folderId)
            .Select(rule => rule.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;

    private void NormalizeFolderOrder(Guid? parentId)
    {
        var siblings = _navigationFolders
            .Where(folder => folder.ParentId == parentId)
            .OrderBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var order = 0; order < siblings.Length; order++)
        {
            var index = GetFolderIndex(siblings[order].Id);
            _navigationFolders[index] = _navigationFolders[index] with { SortOrder = order };
        }
    }

    private void NormalizeRuleOrder(Guid folderId)
    {
        var rules = _rules
            .Where(rule => rule.NavigationFolderId == folderId)
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var order = 0; order < rules.Length; order++)
        {
            var index = GetRuleIndex(rules[order].Id);
            _rules[index] = _rules[index] with { SortOrder = order };
        }
    }

    private static IReadOnlyList<string> NormalizeAliases(IEnumerable<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        foreach (var alias in aliases)
        {
            var value = alias?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                continue;
            }

            if (seen.Add(AliasNormalizer.Normalize(value)))
            {
                values.Add(value);
            }
        }

        return values.Count > 0
            ? values.ToArray()
            : throw new InvalidOperationException("规则至少需要一个关键词。");
    }

    private static string ValidatePath(string folderPath)
    {
        var path = folderPath?.Trim() ?? string.Empty;
        return path.Length > 0
            ? path
            : throw new InvalidOperationException("规则的文件夹路径不能为空。");
    }

    private DateTimeOffset UtcNow() =>
        _timeProvider.GetUtcNow().ToUniversalTime();
}
