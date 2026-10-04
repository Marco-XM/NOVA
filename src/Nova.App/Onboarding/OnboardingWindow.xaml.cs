using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nova.App.Settings;
using Nova.Core.Settings;
using Wpf.Ui.Controls;

namespace Nova.App.Onboarding;

/// <summary>First-run flow: welcome → monitor → animation → media → startup → done.</summary>
public partial class OnboardingWindow : FluentWindow
{
    private readonly AppHost _host;
    private readonly SettingsViewModel _vm;
    private readonly FrameworkElement[] _steps;
    private int _index;

    public OnboardingWindow(AppHost host)
    {
        _host = host;
        _vm = new SettingsViewModel(host);
        DataContext = _vm;
        InitializeComponent();
        _steps = new FrameworkElement[] { Step1, Step2, Step3, Step4, Step5, Step6 };
        for (var i = 0; i < _steps.Length; i++)
        {
            Dots.Children.Add(new Rectangle { Width = 8, Height = 8, RadiusX = 4, RadiusY = 4, Margin = new Thickness(0, 0, 6, 0) });
        }
        // Recommended default for a utility like this; the user sees and can change it on step 5.
        if (!host.Services.Settings.Current.FirstRunCompleted && !host.Services.Startup.IsEnabled) _vm.StartWithWindows = true;
        Show(0, animate: false);
        Closed += (_, _) => _vm.Dispose();
    }

    private void Show(int index, bool animate = true)
    {
        var forward = index >= _index;
        _index = Math.Clamp(index, 0, _steps.Length - 1);
        for (var i = 0; i < _steps.Length; i++) _steps[i].Visibility = i == _index ? Visibility.Visible : Visibility.Collapsed;

        var last = _index == _steps.Length - 1;
        BackButton.Visibility = _index == 0 || last ? Visibility.Collapsed : Visibility.Visible;
        SkipButton.Visibility = _index == 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _index switch { 0 => "Get started", 4 => "Finish", 5 => "Done", _ => "Next" };

        for (var i = 0; i < Dots.Children.Count; i++)
        {
            var dot = (Rectangle)Dots.Children[i];
            dot.Width = i == _index ? 22 : 8;
            dot.SetResourceReference(Shape.FillProperty, i == _index ? "AccentFillColorDefaultBrush" : "ControlStrongFillColorDisabledBrush");
        }

        if (!animate) return;
        var step = _steps[_index];
        var shift = new TranslateTransform(forward ? 28 : -28, 0);
        step.RenderTransform = shift;
        var duration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        step.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(shift.X, 0, duration) { EasingFunction = ease });
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_index == _steps.Length - 2) Complete();
        if (_index == _steps.Length - 1) { Close(); return; }
        Show(_index + 1);
    }

    private void OnBack(object sender, RoutedEventArgs e) => Show(_index - 1);

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        Complete();
        Close();
    }

    private void Complete()
    {
        var settings = _host.Services.Settings;
        settings.Current.FirstRunCompleted = true;
        settings.Update(SettingsSection.None);
        settings.SaveNow(force: true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}
