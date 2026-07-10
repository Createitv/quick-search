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
}
