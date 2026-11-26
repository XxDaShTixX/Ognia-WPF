using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Ognia.Wpf.ViewModels.Shell;

namespace Ognia.Wpf.Views.Shell;

public partial class MiniPlayerView : UserControl
{
    public MiniPlayerView()
    {
        InitializeComponent();
    }

    private void Slider_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (DataContext is PlaybackBarViewModel vm)
            vm.PlaybackViewModel.BeginSeek();
    }

    private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is PlaybackBarViewModel vm && sender is Slider slider)
            vm.PlaybackViewModel.EndSeek(TimeSpan.FromSeconds(slider.Value));
    }
}