namespace QuickSearch.Core;

public readonly record struct AppReleaseVersion : IComparable<AppReleaseVersion>
{
    private readonly Version _value;

    private AppReleaseVersion(Version value)
    {
        _value = value;
    }

    public static AppReleaseVersion Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("版本号不能为空。");
        }

        var normalized = value.Trim().TrimStart('v', 'V');
        var buildSeparator = normalized.IndexOf('+');
        if (buildSeparator >= 0)
        {
            normalized = normalized[..buildSeparator];
        }

        if (normalized.Contains('-', StringComparison.Ordinal)
            || !Version.TryParse(normalized, out var parsed)
            || parsed.Major < 0
            || parsed.Minor < 0
            || parsed.Build < 0
            || parsed.Revision >= 0)
        {
            throw new FormatException($"不是有效的正式版本号：{value}");
        }

        return new AppReleaseVersion(parsed);
    }

    public bool IsNewerThan(AppReleaseVersion installedVersion) =>
        CompareTo(installedVersion) > 0;

    public int CompareTo(AppReleaseVersion other) => _value.CompareTo(other._value);

    public override string ToString() =>
        $"{_value.Major}.{_value.Minor}.{_value.Build}";
}
