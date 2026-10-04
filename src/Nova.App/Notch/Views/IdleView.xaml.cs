using System.Windows.Controls;

namespace Nova.App.Notch.Views;

public partial class IdleView : UserControl, IFluidView
{
    public IdleView() => InitializeComponent();

    /// <summary>The wordmark brightens a little while the pointer is over the notch.</summary>
    public void SetHover(bool hover) => Wordmark.Opacity = hover ? 0.95 : 0.62;
}
