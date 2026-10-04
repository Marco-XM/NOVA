using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Nova.App.Notch.Rendering;

/// <summary>
/// Soft drop shadow under the notch: the outline filled with the shadow color, offset downward and
/// blurred on the GPU. Kept separate from <see cref="NotchSurface"/> so the accent glow isn't darkened.
/// </summary>
public sealed class NotchShadow : FrameworkElement
{
    private Geometry? _geometry;
    private Brush _brush = Brushes.Black;
    private readonly BlurEffect _blur = new() { Radius = 26, RenderingBias = RenderingBias.Performance, KernelType = KernelType.Gaussian };
    private readonly TranslateTransform _offset = new(0, 6);

    public NotchShadow()
    {
        IsHitTestVisible = false;
        Effect = _blur;
    }

    public void SetGeometry(Geometry? geometry)
    {
        _geometry = geometry;
        InvalidateVisual();
    }

    public void Configure(Color color, double opacity, double softness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _brush = brush;
        Opacity = opacity;
        _blur.Radius = 14 + 22 * softness;
        _offset.Y = 3 + 4 * softness;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_geometry is null) return;
        dc.PushTransform(_offset);
        dc.DrawGeometry(_brush, null, _geometry);
        dc.Pop();
    }
}
