using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Nova.App.Notch.Views;

public partial class MediaView : UserControl
{
    public MediaView()
    {
        InitializeComponent();
        // Seek only when the user lets go, so live progress updates never fight the drag.
        ProgressSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => Seek()));
        ProgressSlider.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(Seek);
        VolumeSlider.PreviewMouseLeftButtonUp += (_, _) => (DataContext as NotchViewModel)?.CommitSessionVolume();
        VolumeSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => (DataContext as NotchViewModel)?.CommitSessionVolume()));
    }

    private void Seek() => (DataContext as NotchViewModel)?.SeekTo(ProgressSlider.Value);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        (DataContext as NotchViewModel)?.AdjustSessionVolume(e.Delta > 0 ? 0.04 : -0.04);
        e.Handled = true;
    }
}
