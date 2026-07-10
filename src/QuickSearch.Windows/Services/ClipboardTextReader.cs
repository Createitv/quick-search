using QuickSearch.Core;

namespace QuickSearch.Windows;

public sealed class ClipboardTextReader : IClipboardTextReader
{
    public PlatformOperationResult<string?> ReadText() =>
        PlatformBoundary.Capture(
            () => System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText().Trim()
                : null,
            "无法读取剪贴板");
}
