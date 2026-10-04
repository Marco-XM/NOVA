using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nova.App.Settings.Pages;
using Wpf.Ui.Controls;

namespace Nova.App.Settings;

public sealed record NavEntry(string Id, string Title, string Glyph, string Subtitle, Func<FrameworkElement> Create);

/// <summary>The control center window. Pages are created lazily and cached while the window is open.</summary>
public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsViewModel _vm;
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private readonly List<NavEntry> _entries;

    public SettingsWindow(AppHost host)
    {
        _vm = new SettingsViewModel(host);
        DataContext = _vm;
        InitializeComponent();
        _entries = new List<NavEntry>
        {
            new("overview", "Overview", "", "NOVA at a glance.", () => new OverviewPage()),
            new("appearance", "Appearance", "", "Material, color, size and shape of the notch.", () => new AppearancePage()),
            new("animations", "Animations", "", "Choose how the notch moves. Every preview below is live.", () => new AnimationsPage()),
            new("monitor", "Monitor", "", "Which display shows the notch, and how it follows you.", () => new MonitorPage()),
            new("media", "Media", "", "Now playing from Spotify, Chrome and other Windows media apps.", () => new MediaPage()),
            new("quickapps", "Quick Apps", "", "Apps, folders and sites you can launch from the notch.", () => new QuickAppsPage()),
            new("behavior", "Behavior", "", "Startup, hover, shortcut and fullscreen behavior.", () => new BehaviorPage()),
            new("notifications", "Notifications", "", "Which events briefly expand the notch.", () => new NotificationsPage()),
            new("modules", "Extras", "", "Timer, calculator, clipboard, search and system indicators.", () => new ModulesPage()),
            new("advanced", "Advanced", "", "Diagnostics, performance, logs and reset.", () => new AdvancedPage()),
        };
        Nav.ItemsSource = _entries;
        Nav.SelectedIndex = 0;
        Closed += (_, _) => _vm.Dispose();
        // Live previews stop rendering while the window is minimized.
        StateChanged += (_, _) => PageHost.Visibility = WindowState == WindowState.Minimized ? Visibility.Hidden : Visibility.Visible;
    }

    public void Navigate(string? id)
    {
        var index = id is null ? -1 : _entries.FindIndex(e => e.Id == id);
        if (index >= 0) Nav.SelectedIndex = index;
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavEntry entry) return;
        if (!_pages.TryGetValue(entry.Id, out var page))
        {
            page = entry.Create();
            page.DataContext = _vm;
            _pages[entry.Id] = page;
        }
        PageTitle.Text = entry.Title;
        PageSubtitle.Text = entry.Subtitle;
        PageHost.Content = page;
        Scroller.ScrollToTop();

        // Gentle entrance: fade + rise.
        var transform = new TranslateTransform(0, 14);
        page.RenderTransform = transform;
        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = ease });
    }
}
