namespace QuickSearch.Core.Tests;

public sealed class MappingGroupEditorViewModelTests
{
    [Fact]
    public void ParseKeywords_AcceptsAllSeparatorsAndKeepsFirstDisplayForm()
    {
        var group = new MappingGroupEditorViewModel(
            "你好，今天好; hello\nHELLO； 你好 ",
            @"D:\资料");

        Assert.Equal(
            ["你好", "今天好", "hello"],
            group.ParseKeywords());
    }

    [Fact]
    public void ParseKeywords_RemovesEmptyValuesAndNormalizesInternalWhitespaceForDeduplication()
    {
        var group = new MappingGroupEditorViewModel(
            "  Sales   Team ,, sales\tteam；；Support  ",
            @"D:\Clients");

        Assert.Equal(
            ["Sales   Team", "Support"],
            group.ParseKeywords());
    }

    [Fact]
    public void KeywordConstructor_UsesReadableChineseCommaDisplay()
    {
        var group = new MappingGroupEditorViewModel(
            ["你好", "今天好", "hello"],
            @"D:\资料");

        Assert.Equal("你好， 今天好， hello", group.KeywordsText);
        Assert.Equal(@"D:\资料", group.FolderPath);
    }

    [Theory]
    [InlineData("今天", true)]
    [InlineData("HELLO", true)]
    [InlineData("资料", true)]
    [InlineData("missing", false)]
    [InlineData("", true)]
    public void Matches_SearchesKeywordsAndPathIgnoringCase(
        string filter,
        bool expected)
    {
        var group = new MappingGroupEditorViewModel(
            "今天好， hello",
            @"D:\资料\Greeting");

        Assert.Equal(expected, group.Matches(filter));
    }

    [Fact]
    public void EmptyConstructor_StartsWithBlankEditableValues()
    {
        var group = new MappingGroupEditorViewModel();

        Assert.Equal(string.Empty, group.KeywordsText);
        Assert.Equal(string.Empty, group.FolderPath);
        Assert.Empty(group.ParseKeywords());
    }
}

