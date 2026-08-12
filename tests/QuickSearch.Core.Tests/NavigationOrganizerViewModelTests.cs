namespace QuickSearch.Core.Tests;

public sealed class NavigationOrganizerViewModelTests
{
    [Fact]
    public void StagedChanges_DoNotMutateLiveConfigurationUntilCandidateIsApplied()
    {
        var live = new AppConfiguration();
        var organizer = new NavigationOrganizerViewModel(live);

        var created = organizer.CreateFolder(null, "客户");

        Assert.DoesNotContain(live.NavigationFolders, folder => folder.Name == "客户");
        Assert.Contains(organizer.Folders, folder => folder.Id == created.Id);
        Assert.Contains(organizer.BuildCandidate().NavigationFolders, folder => folder.Id == created.Id);
    }

    [Fact]
    public void DeleteFolderAndMoveContents_ReparentsDirectChildrenAndRules()
    {
        var live = new AppConfiguration();
        var customer = live.AddNavigationFolder("客户", null);
        var project = live.AddNavigationFolder("项目", customer.Id);
        var archive = live.AddNavigationFolder("归档", project.Id);
        var rule = live.AddRule("项目文档", ["docs"], "/docs", customer.Id);
        var organizer = new NavigationOrganizerViewModel(live);

        organizer.DeleteFolder(customer.Id, FolderDeletionMode.MoveContentsToParent);

        var candidate = organizer.BuildCandidate();
        Assert.Equal(candidate.UncategorizedFolderId, candidate.NavigationFolders.Single(folder => folder.Id == project.Id).ParentId);
        Assert.Equal(project.Id, candidate.NavigationFolders.Single(folder => folder.Id == archive.Id).ParentId);
        Assert.Equal(candidate.UncategorizedFolderId, candidate.Rules.Single(item => item.Id == rule.Id).NavigationFolderId);
        Assert.DoesNotContain(candidate.NavigationFolders, folder => folder.Id == customer.Id);
    }

    [Fact]
    public void MoveFolder_RejectsCyclesWithoutChangingStagedTree()
    {
        var live = new AppConfiguration();
        var parent = live.AddNavigationFolder("客户", null);
        var child = live.AddNavigationFolder("项目", parent.Id);
        var organizer = new NavigationOrganizerViewModel(live);

        Assert.Throws<InvalidOperationException>(() =>
            organizer.MoveFolder(parent.Id, child.Id, 0));

        Assert.Null(organizer.Folders.Single(folder => folder.Id == parent.Id).ParentId);
    }

    [Fact]
    public void MoveRules_MovesEverySelectedRuleToOneCanonicalFolder()
    {
        var live = new AppConfiguration();
        var source = live.AddNavigationFolder("待整理", null);
        var target = live.AddNavigationFolder("归档", null);
        var first = live.AddRule("一", ["one"], "/one", source.Id);
        var second = live.AddRule("二", ["two"], "/two", source.Id);
        var organizer = new NavigationOrganizerViewModel(live);

        organizer.MoveRules([first.Id, second.Id], target.Id);

        Assert.All(
            organizer.BuildCandidate().Rules.Where(rule => rule.Id == first.Id || rule.Id == second.Id),
            rule => Assert.Equal(target.Id, rule.NavigationFolderId));
    }
}
