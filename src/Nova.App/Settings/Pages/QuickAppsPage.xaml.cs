using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Nova.App.Controls;

namespace Nova.App.Settings.Pages;

public partial class QuickAppsPage : UserControl
{
    private Point _dragStart;
    private QuickAppRow? _dragRow;

    public QuickAppsPage() => InitializeComponent();

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private void OnAddInstalled(object sender, RoutedEventArgs e)
    {
        var picker = new AppPickerWindow { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true && picker.Selected.Count > 0) Vm?.AddQuickApps(picker.Selected);
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: QuickAppRow row } box) Vm?.RenameQuickApp(row, box.Text);
    }

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape && sender is TextBox box)
        {
            if (e.Key == Key.Escape && box.Tag is QuickAppRow row) box.Text = row.Name;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnHotkeyCommitted(object? sender, string value)
    {
        if (sender is HotkeyBox { Tag: QuickAppRow row }) Vm?.SetQuickAppHotkey(row, value);
    }

    private void OnRecordingChanged(object? sender, bool recording)
    {
        if (recording) Vm?.SuspendHotkeys(); else Vm?.ResumeHotkeys();
    }

    // ── drag & drop reordering ──
    private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInteractive(d)) { _dragRow = null; return; }
        _dragStart = e.GetPosition(this);
        _dragRow = (sender as FrameworkElement)?.Tag as QuickAppRow;
    }

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragRow is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _dragRow;
        _dragRow = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(QuickAppRow), row), DragDropEffects.Move);
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(QuickAppRow)) is not QuickAppRow dragged || (sender as FrameworkElement)?.Tag is not QuickAppRow target || Vm is null) return;
        var index = Vm.QuickApps.IndexOf(target);
        if (index >= 0) Vm.MoveTo(dragged, index);
    }

    private static bool IsInteractive(DependencyObject d)
    {
        for (var current = d; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase or TextBox or HotkeyBox) return true;
            if (current is Border { Tag: QuickAppRow }) return false;
        }
        return false;
    }
}
