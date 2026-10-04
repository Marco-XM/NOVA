using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nova.Core.QuickApps;
using Nova.Platform.QuickApps;
using Wpf.Ui.Controls;

namespace Nova.App.Settings;

/// <summary>Lists everything in the Start menu (Win32, Store and web apps) with real icons.</summary>
public partial class AppPickerWindow : FluentWindow
{
    public sealed class Entry(InstalledApp app)
    {
        private ImageSource? _icon;
        private bool _loaded;
        public InstalledApp App { get; } = app;
        public string Name => App.Name;
        // Icons are extracted lazily, only for rows that become visible.
        public ImageSource? Icon
        {
            get
            {
                if (_loaded) return _icon;
                _loaded = true;
                _icon = ShellIcons.ForParsingName("shell:AppsFolder\\" + App.ParsingName, 32);
                return _icon;
            }
        }
    }

    private List<Entry> _all = new();

    public AppPickerWindow()
    {
        InitializeComponent();
        List.SelectionChanged += (_, _) =>
        {
            var n = List.SelectedItems.Count;
            AddButton.IsEnabled = n > 0;
            Count.Text = n == 0 ? "Select one or more apps" : $"{n} selected";
        };
        Loaded += async (_, _) =>
        {
            var apps = await InstalledAppScanner.ScanAsync();
            _all = apps.Select(a => new Entry(a)).ToList();
            List.ItemsSource = _all;
            Loading.Visibility = Visibility.Collapsed;
            if (_all.Count == 0) Count.Text = "No apps found";
            Search.Focus();
        };
    }

    public List<QuickApp> Selected { get; } = new();

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        var q = Search.Text.Trim();
        List.ItemsSource = q.Length == 0 ? _all : _all.Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        Selected.AddRange(List.SelectedItems.Cast<Entry>().Select(entry => entry.App.ToQuickApp()));
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
