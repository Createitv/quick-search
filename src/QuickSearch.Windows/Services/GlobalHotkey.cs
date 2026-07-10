using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public sealed partial class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x5146;
    private readonly HwndSource _source;

    public event EventHandler? Pressed;

    public GlobalHotkey(System.Windows.Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle)
                  ?? throw new InvalidOperationException("Window handle is unavailable.");
        _source.AddHook(WindowProc);
    }

    public bool Register(string shortcut)
    {
        UnregisterHotKey(_source.Handle, HotkeyId);
        var gesture = ShortcutGesture.Parse(shortcut);
        var modifiers = 0u;
        if (gesture.Alt) modifiers |= 0x0001;
        if (gesture.Control) modifiers |= 0x0002;
        if (gesture.Shift) modifiers |= 0x0004;
        if (gesture.Windows) modifiers |= 0x0008;

        var key = gesture.Key == "Space"
            ? Key.Space
            : Enum.Parse<Key>(gesture.Key, ignoreCase: true);
        return RegisterHotKey(
            _source.Handle,
            HotkeyId,
            modifiers | 0x4000,
            (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    public void Dispose()
    {
        UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WindowProc);
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}
