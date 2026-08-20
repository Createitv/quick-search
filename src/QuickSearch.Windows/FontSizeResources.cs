using System.Windows;
using Application = System.Windows.Application;

namespace QuickSearch.Windows;

public static class FontSizeResources
{
    public const double DefaultFontSize = 14;

    public static void Apply(double fontSize)
    {
        var normalized = Math.Clamp(fontSize, 11, 22);
        var resources = Application.Current.Resources;
        resources["AppFontSize"] = normalized;
        resources["AppFontSizeSmall"] = Math.Max(10, normalized - 2);
        resources["AppFontSizeSecondary"] = Math.Max(10, normalized - 1);
        resources["AppFontSizeSection"] = normalized + 2;
        resources["AppFontSizeTitle"] = normalized + 14;
        resources["AppFontSizeDialogTitle"] = normalized + 8;
    }
}
