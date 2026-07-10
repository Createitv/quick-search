using System.ComponentModel;
using System.Windows;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _allowClose;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.HideRequested += ViewModel_HideRequested;
    }

    public void AllowApplicationExit()
    {
        if (_allowClose)
        {
            return;
        }

        _allowClose = true;
        _viewModel.HideRequested -= ViewModel_HideRequested;
        if (IsLoaded)
        {
            Close();
        }
    }

    private void ViewModel_HideRequested(object? sender, EventArgs e) => Hide();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _viewModel.Cancel();
    }
}
