using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.Core.Monitors;

namespace Nova.App.Controls;

/// <summary>"Identify" overlay: shows each monitor's number and name in its center for a few seconds.</summary>
public static class MonitorIdentifier
{
    public static void Show(IReadOnlyList<MonitorInfo> monitors)
    {
        var index = 1;
        foreach (var monitor in monitors)
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Width = 300,
                Height = 200,
                Left = -10000,
                Top = -10000,
                Content = new Border
                {
                    CornerRadius = new CornerRadius(24),
                    Background = new SolidColorBrush(Color.FromArgb(235, 16, 17, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = index.ToString(), FontSize = 84, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") },
                            new TextBlock { Text = monitor.DisplayLabel, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(170, 175, 190)), HorizontalAlignment = HorizontalAlignment.Center },
                            new TextBlock { Text = monitor.Description, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(120, 125, 140)), HorizontalAlignment = HorizontalAlignment.Center },
                        },
                    },
                },
            };
            var m = monitor;
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var rect = new PixelRect(
                    m.Bounds.Left + (m.Bounds.Width - DpiMath.ToPhysical(300, m.Dpi)) / 2,
                    m.Bounds.Top + (m.Bounds.Height - DpiMath.ToPhysical(200, m.Dpi)) / 2,
                    DpiMath.ToPhysical(300, m.Dpi), DpiMath.ToPhysical(200, m.Dpi));
                SetWindowPos(hwnd, new IntPtr(-1), rect.Left, rect.Top, rect.Width, rect.Height, 0x10 | 0x40);
            };
            window.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
            timer.Start();
            index++;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
