using System.Windows.Controls;
using Ognia.Wpf.ViewModels.RightPane;

namespace Ognia.Wpf.Views.RightPane;

public partial class MultiTrackActionsView : UserControl
{
    public MultiTrackActionsView()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MultiTrackSelectionViewModel viewModel)
            viewModel.ResetTargets();
    }
}