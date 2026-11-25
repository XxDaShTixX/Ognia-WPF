using System.Windows;
using System.Windows.Controls;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Services;
using Ognia.Wpf.ViewModels;

namespace Ognia.Wpf.Views.Auth;

public partial class ResetPasswordView : UserControl
{
    private readonly ResetPasswordViewModel _vm;

    public ResetPasswordView(ApiClientService api, IAppNavigator nav)
    {
        InitializeComponent();

        _vm = new ResetPasswordViewModel(api, nav);
        DataContext = _vm;
    }

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.NewPassword = ((PasswordBox)sender).Password;
    }
}