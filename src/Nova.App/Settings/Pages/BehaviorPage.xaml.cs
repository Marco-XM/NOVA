using System.Windows.Controls;

namespace Nova.App.Settings.Pages;

public partial class BehaviorPage : UserControl
{
    public BehaviorPage() => InitializeComponent();

    /// <summary>Global hotkeys would swallow the keys being recorded, so pause them meanwhile.</summary>
    private void OnRecordingChanged(object? sender, bool recording)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (recording) vm.SuspendHotkeys(); else vm.ResumeHotkeys();
    }
}
