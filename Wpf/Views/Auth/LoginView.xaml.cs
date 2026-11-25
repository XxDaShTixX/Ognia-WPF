using System.Windows.Controls;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Services;
using Ognia.Wpf.ViewModels;

namespace Ognia.Wpf.Views.Auth;

public partial class LoginView : UserControl
{
    private readonly LoginViewModel _vm;

    public LoginView(ApiClientService api, IAppNavigator nav)
    {
        InitializeComponent();

        _vm = new LoginViewModel(api, nav);
        DataContext = _vm;
    }

    private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        => _vm.Password = ((PasswordBox)sender).Password;

    private void RegPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        => _vm.RegPassword = ((PasswordBox)sender).Password;
}