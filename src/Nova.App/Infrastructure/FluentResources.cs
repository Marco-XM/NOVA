using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace Nova.App.Infrastructure;

/// <summary>
/// Loads the WPF-UI (Fluent) theme dictionaries the first time a full window opens. The background
/// notch never needs them, so not loading them at startup keeps idle memory and startup time down.
/// </summary>
public static class FluentResources
{
    private static bool _loaded;

    public static void Ensure()
    {
        if (_loaded) return;
        _loaded = true;
        var resources = Application.Current.Resources.MergedDictionaries;
        resources.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        resources.Add(new ControlsDictionary());
        resources.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Settings/SettingsStyles.xaml") });
        ApplicationAccentColorManager.Apply(Color.FromRgb(88, 104, 240), ApplicationTheme.Dark);
    }
}
