using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public sealed partial class GlobalHotkey : IHotkeyRegistration, IDisposable
{
    private readonly HwndSource _source;
    private readonly HotkeyRegistrationController _registrations;

    public event EventHandler? Pressed;

    public GlobalHotkey(System.Windows.Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle)
                  ?? throw new InvalidOperationException("Window handle is unavailable.");
        _source.AddHook(WindowProc);
        _registrations = new HotkeyRegistrationController(
            new Win32HotkeyRegistrar(_source.Handle));
    }

    public string? ActiveShortcut => _registrations.ActiveShortcut;

    public PlatformOperationResult TryReplace(string shortcut) =>
        _registrations.TryReplace(shortcut);

    public void Dispose()
    {
        _registrations.Dispose();
        _source.RemoveHook(WindowProc);
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && _registrations.IsActiveRegistration((int)wParam))
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    private sealed class Win32HotkeyRegistrar(nint handle) : IHotkeyRegistrar
    {
        public bool TryRegister(int registrationId, string shortcut)
        {
            var gesture = ShortcutGesture.Parse(shortcut);
            var modifiers = 0u;
            if (gesture.Alt) modifiers |= 0x0001;
            if (gesture.Control) modifiers |= 0x0002;
            if (gesture.Shift) modifiers |= 0x0004;
            if (gesture.Windows) modifiers |= 0x0008;

            var key = Enum.Parse<Key>(
                ShortcutKeyTranslator.ToWpfKeyName(gesture.Key),
                ignoreCase: true);
            return RegisterHotKey(
                handle,
                registrationId,
                modifiers | 0x4000,
                (uint)KeyInterop.VirtualKeyFromKey(key));
        }

        public void Unregister(int registrationId) =>
            UnregisterHotKey(handle, registrationId);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}
