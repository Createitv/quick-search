namespace QuickSearch.Core;

public static class StartupCommand
{
    public static string Build(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath}\" --background";
    }
}
