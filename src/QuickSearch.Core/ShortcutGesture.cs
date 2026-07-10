namespace QuickSearch.Core;

public sealed record ShortcutGesture(
    bool Control,
    bool Alt,
    bool Shift,
    bool Windows,
    string Key)
{
    public static ShortcutGesture Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var parts = text.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var control = false;
        var alt = false;
        var shift = false;
        var windows = false;
        string? key = null;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    control = true;
                    break;
                case "ALT":
                    alt = true;
                    break;
                case "SHIFT":
                    shift = true;
                    break;
                case "WIN":
                case "WINDOWS":
                    windows = true;
                    break;
                default:
                    if (key is not null || !IsSupportedKey(part))
                    {
                        throw new FormatException($"Unsupported shortcut: {text}");
                    }

                    key = CanonicalizeKey(part);
                    break;
            }
        }

        if (key is null || !(control || alt || shift || windows))
        {
            throw new FormatException($"Shortcut requires a modifier and key: {text}");
        }

        return new ShortcutGesture(control, alt, shift, windows, key);
    }

    private static bool IsSupportedKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            return true;
        }

        if (string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return key.Length is 2 or 3
               && key[0] is 'F' or 'f'
               && int.TryParse(key[1..], out var functionKey)
               && functionKey is >= 1 and <= 24;
    }

    private static string CanonicalizeKey(string key)
    {
        return string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase)
            ? "Space"
            : key.ToUpperInvariant();
    }
}
