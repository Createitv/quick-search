using System.Windows;
using QuickSearch.Core;

namespace QuickSearch.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppConfiguration _configuration;
    private readonly IMappingStore _store;
    private readonly StartupRegistration _startup;

    public SettingsWindow(
        AppConfiguration configuration,
        IMappingStore store,
        StartupRegistration startup)
    {
        _configuration = configuration;
        _store = store;
        _startup = startup;
        InitializeComponent();
        ShortcutBox.Text = configuration.Settings.GlobalShortcut;
        StartupCheck.IsChecked = configuration.Settings.StartWithWindows;
        RefreshMappings();
    }

    private void RefreshMappings()
    {
        MappingsList.ItemsSource = null;
        MappingsList.ItemsSource = _configuration.Mappings
            .OrderBy(mapping => mapping.Alias, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MappingsList.SelectedItem is not FolderMapping selected)
        {
            return;
        }

        _configuration.RemoveMapping(selected.Alias);
        await _store.SaveAsync(_configuration);
        RefreshMappings();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var gesture = ShortcutGesture.Parse(ShortcutBox.Text);
            var shortcut = BuildShortcutText(gesture);
            var startWithWindows = StartupCheck.IsChecked == true;
            _configuration.Settings = _configuration.Settings with
            {
                GlobalShortcut = shortcut,
                StartWithWindows = startWithWindows
            };
            await _store.SaveAsync(_configuration);
            _startup.SetEnabled(startWithWindows);
            DialogResult = true;
        }
        catch (FormatException exception)
        {
            System.Windows.MessageBox.Show(
                this,
                exception.Message,
                "快捷键无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string BuildShortcutText(ShortcutGesture gesture)
    {
        var parts = new List<string>();
        if (gesture.Control) parts.Add("Ctrl");
        if (gesture.Alt) parts.Add("Alt");
        if (gesture.Shift) parts.Add("Shift");
        if (gesture.Windows) parts.Add("Win");
        parts.Add(gesture.Key);
        return string.Join('+', parts);
    }
}
