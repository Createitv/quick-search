using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickSearch.Core;
using Button = System.Windows.Controls.Button;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace QuickSearch.Windows;

public partial class QuickLauncherWindow : Window, IDisposable
{
    public static readonly DependencyProperty ResultItemWidthProperty =
        DependencyProperty.Register(
            nameof(ResultItemWidth),
            typeof(double),
            typeof(QuickLauncherWindow),
            new PropertyMetadata(560d));

    private readonly QuickLauncherViewModel _viewModel;
    private bool _allowClose;
    private bool _openingSettings;
    private bool _hideWhenDeactivated = true;
    private bool _hasBeenShown;
    private Point _resultDragStart;
    private QuickLauncherResult? _draggedResult;

    public QuickLauncherWindow(QuickLauncherViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.HideRequested += ViewModel_HideRequested;
        _viewModel.SettingsRequested += ViewModel_SettingsRequested;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public event EventHandler? SettingsRequested;

    public double ResultItemWidth
    {
        get => (double)GetValue(ResultItemWidthProperty);
        set => SetValue(ResultItemWidthProperty, value);
    }

    public void ShowLauncher(bool hideWhenDeactivated = true)
    {
        _openingSettings = false;
        _hideWhenDeactivated = hideWhenDeactivated;
        _viewModel.Reset();
        _viewModel.PrefillFromClipboard();
        if (!_hasBeenShown)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + Math.Max(16, (workArea.Width - Width) / 2);
            Top = workArea.Top + Math.Max(48, workArea.Height * 0.16);
            _hasBeenShown = true;
        }

        Show();
        Activate();
        LauncherSearchBox.Focus();
        Keyboard.Focus(LauncherSearchBox);
        UpdateResultItemWidth();
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
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
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

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed
            || IsDescendantOf(e.OriginalSource as DependencyObject, LauncherSearchBox))
        {
            return;
        }

        DragMove();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResultItemWidth();

    private void ResultsList_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResultItemWidth();

    private void Layout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private async void LayoutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string value
            || !int.TryParse(value, out var columns))
        {
            return;
        }

        try
        {
            await _viewModel.SetResultColumnsAsync(columns);
            UpdateResultItemWidth();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"无法保存布局：{exception.Message}",
                "结果布局",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        _viewModel.RequestSettings();

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_hideWhenDeactivated && !_openingSettings)
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

    private void Result_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _resultDragStart = e.GetPosition(this);
        _draggedResult = (sender as FrameworkElement)?.DataContext as QuickLauncherResult;
    }

    private void Result_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedResult is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _resultDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _resultDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        try
        {
            DragDrop.DoDragDrop(
                (DependencyObject)sender,
                _draggedResult,
                DragDropEffects.Move);
        }
        finally
        {
            _draggedResult = null;
        }
    }

    private void Result_DragOver(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not QuickLauncherResult target
            || e.Data.GetData(typeof(QuickLauncherResult)) is not QuickLauncherResult source
            || ReferenceEquals(source, target))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        if (sender is FrameworkElement element)
        {
            element.Opacity = 0.74;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Result_DragLeave(object sender, DragEventArgs e) =>
        ResetResultDropIndicator(sender as FrameworkElement);

    private void Result_Drop(object sender, DragEventArgs e)
    {
        ResetResultDropIndicator(sender as FrameworkElement);
        if ((sender as FrameworkElement)?.DataContext is not QuickLauncherResult target
            || e.Data.GetData(typeof(QuickLauncherResult)) is not QuickLauncherResult source)
        {
            return;
        }

        _viewModel.ReorderResult(source, target);
        _draggedResult = null;
        e.Handled = true;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(QuickLauncherViewModel.ResultColumns), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(QuickLauncherViewModel.Results), StringComparison.Ordinal))
        {
            UpdateResultItemWidth();
        }
    }

    private void UpdateResultItemWidth()
    {
        if (ResultsList.ActualWidth <= 0)
        {
            return;
        }

        var columns = Math.Clamp(_viewModel.ResultColumns, 1, 3);
        var available = Math.Max(240, ResultsList.ActualWidth - 16);
        var gutter = columns > 1 ? (columns - 1) * 8 : 0;
        ResultItemWidth = Math.Max(210, Math.Floor((available - gutter) / columns));
    }

    private static void ResetResultDropIndicator(FrameworkElement? element)
    {
        if (element is not null)
        {
            element.Opacity = 1;
        }
    }

    private static bool IsDescendantOf(DependencyObject? source, DependencyObject target)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, target))
            {
                return true;
            }

            try
            {
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            catch (InvalidOperationException)
            {
                source = LogicalTreeHelper.GetParent(source);
            }
        }

        return false;
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
