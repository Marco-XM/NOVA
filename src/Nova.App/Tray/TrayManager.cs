using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Win32;
using Nova.Core.Logging;

namespace Nova.App.Tray;

/// <summary>System tray icon with NOVA's styled context menu (built from <see cref="TrayMenuModel"/>).</summary>
public sealed class TrayManager : IDisposable
{
    private readonly ITrayActions _actions;
    private readonly TaskbarIcon _icon;
    private readonly ContextMenu _menu;
    private bool? _lightTaskbar;

    public TrayManager(ITrayActions actions)
    {
        _actions = actions;
        _menu = new ContextMenu { Style = (Style)Application.Current.FindResource("NovaContextMenu") };
        _menu.Resources.Add(typeof(MenuItem), Application.Current.FindResource("NovaMenuItem"));
        _menu.Resources.Add(typeof(Separator), Application.Current.FindResource("NovaMenuSeparator"));
        _menu.Opened += (_, _) => Rebuild();

        _icon = new TaskbarIcon
        {
            ToolTipText = "NOVA",
            ContextMenu = _menu,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
        };
        _icon.TrayLeftMouseUp += (_, _) => Safe(() => _actions.OpenSettings());
        UpdateIcon();
        Rebuild();
        _icon.ForceCreate(false);
    }

    /// <summary>Picks a dark or light glyph to match the taskbar theme.</summary>
    public void UpdateIcon()
    {
        var light = IsTaskbarLight();
        if (_lightTaskbar == light) return;
        _lightTaskbar = light;
        var uri = new Uri(light ? "pack://application:,,,/Assets/nova-tray-light.ico" : "pack://application:,,,/Assets/nova-tray.ico");
        _icon.IconSource = BitmapFrame.Create(uri);
    }

    private static bool IsTaskbarLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    private void Rebuild()
    {
        _menu.Items.Clear();
        foreach (var entry in TrayMenuModel.Build(_actions)) _menu.Items.Add(CreateItem(entry));
    }

    private object CreateItem(TrayEntry entry)
    {
        switch (entry.Kind)
        {
            case TrayEntryKind.Separator:
                return new Separator();
            case TrayEntryKind.Header:
                return new MenuItem { Header = entry.Text, Style = (Style)Application.Current.FindResource("NovaMenuHeader") };
            default:
                var item = new MenuItem
                {
                    Header = entry.Text,
                    Tag = entry.Glyph,
                    IsChecked = entry.IsChecked,
                    IsEnabled = entry.IsEnabled,
                };
                if (entry.Children != null)
                {
                    foreach (var child in entry.Children) item.Items.Add(CreateItem(child));
                }
                else
                {
                    var id = entry.Id;
                    item.Click += (_, _) => Safe(() => TrayMenuModel.Execute(id, _actions));
                }
                return item;
        }
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Error("Tray command failed", ex); }
    }

    public void Dispose()
    {
        _icon.Dispose();
    }
}
