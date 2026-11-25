using Ognia.Wpf.Navigation;
using Ognia.Wpf.Services;
using Ognia.Wpf.ViewModels.Auth;
using System.Windows.Controls;

namespace Ognia.Wpf.Views.Auth;

public partial class LandingView : UserControl
{
    private readonly LandingViewModel _vm;

    public LandingView(ApiClientService api, IAppNavigator nav)
    {
        InitializeComponent();

        _vm = new LandingViewModel(api, nav);
        DataContext = _vm;
    }
}
