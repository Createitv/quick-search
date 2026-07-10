namespace QuickSearch.Core;

public static class ShortcutKeyTranslator
{
    public static string ToWpfKeyName(string shortcutKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutKey);

        return shortcutKey.Length == 1 && char.IsAsciiDigit(shortcutKey[0])
            ? $"D{shortcutKey}"
            : shortcutKey;
    }
}
