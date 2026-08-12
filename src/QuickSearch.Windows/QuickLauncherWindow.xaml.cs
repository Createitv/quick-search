using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using QuickSearch.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace QuickSearch.Windows;

public partial class QuickLauncherWindow : Window, IDisposable
{
    private readonly QuickLauncherViewModel _viewModel;
    private bool _allowClose;
    private bool _openingSettings;

    public QuickLauncherWindow(QuickLauncherViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.HideRequested += ViewModel_HideRequested;
        _viewModel.SettingsRequested += ViewModel_SettingsRequested;
    }

    public event EventHandler? SettingsRequested;

    public void ShowLauncher()
    {
        _openingSettings = false;
        _viewModel.Reset();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(16, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(48, workArea.Height * 0.16);
        Show();
        Activate();
        LauncherSearchBox.Focus();
        Keyboard.Focus(LauncherSearchBox);
    }

    public void AllowApplicationExit()
    {
        _allowClose = true;
        Close();
    }

    public void Dispose()
    {
        _viewModel.HideRequested -= ViewModel_HideRequested;
        _viewModel.SettingsRequested -= ViewModel_SettingsRequested;
        _viewModel.Dispose();
    }

    private void ViewModel_HideRequested(object? sender, EventArgs e) => Hide();

    private void ViewModel_SettingsRequested(object? sender, EventArgs e)
    {
        _openingSettings = true;
        Hide();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control
            && GetShortcutNumber(e.Key) is int number)
        {
            e.Handled = await _viewModel.OpenShortcutAsync(number);
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = await _viewModel.OpenSelectedAsync();
            return;
        }

        if (e.Key is Key.Down or Key.Up && _viewModel.Results.Count > 0)
        {
            var current = ResultsList.SelectedIndex;
            ResultsList.SelectedIndex = e.Key == Key.Down
                ? Math.Min(_viewModel.Results.Count - 1, current + 1)
                : Math.Max(0, current <= 0 ? 0 : current - 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
    }

    private async void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        await _viewModel.OpenSelectedAsync();

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        _viewModel.RequestSettings();

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_openingSettings)
        {
            Hide();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private static int? GetShortcutNumber(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => null
    };
}
