namespace QuickSearch.Core.Tests;

public sealed class ShortcutGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F", true, true, false, "F")]
    [InlineData("Ctrl+Shift+Space", true, false, true, "Space")]
    public void Parse_ReturnsModifiersAndKey(
        string text,
        bool control,
        bool alt,
        bool shift,
        string key)
    {
        var gesture = ShortcutGesture.Parse(text);

        Assert.Equal(control, gesture.Control);
        Assert.Equal(alt, gesture.Alt);
        Assert.Equal(shift, gesture.Shift);
        Assert.Equal(key, gesture.Key);
    }

    [Theory]
    [InlineData("F")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+NoSuchKey")]
    public void Parse_RejectsUnsafeOrIncompleteShortcut(string text)
    {
        Assert.Throws<FormatException>(() => ShortcutGesture.Parse(text));
    }

    [Fact]
    public void ToWpfKeyName_TranslatesDigitToWpfDKey()
    {
        var gesture = ShortcutGesture.Parse("Ctrl+Alt+1");

        var keyName = ShortcutKeyTranslator.ToWpfKeyName(gesture.Key);

        Assert.Equal("D1", keyName);
    }

    [Theory]
    [InlineData("A", "A")]
    [InlineData("Space", "Space")]
    [InlineData("F1", "F1")]
    [InlineData("F24", "F24")]
    public void ToWpfKeyName_PreservesExistingSupportedKeyNames(
        string shortcutKey,
        string expected)
    {
        Assert.Equal(expected, ShortcutKeyTranslator.ToWpfKeyName(shortcutKey));
    }
}
