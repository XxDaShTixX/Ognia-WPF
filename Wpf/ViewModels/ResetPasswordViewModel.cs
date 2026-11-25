using Ognia.Wpf.Models.Auth;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Services;

namespace Ognia.Wpf.ViewModels;

public class ResetPasswordViewModel : ViewModelBase
{
    private readonly ApiClientService _api;
    private readonly IAppNavigator _nav;

    public ResetPasswordViewModel(ApiClientService api, IAppNavigator nav)
    {
        // Initialize
        _api = api;
        _nav = nav;

        // Instantiate Commands
        RequestTokenCommand = new RelayCommand(
            async _ => await RequestTokenAsync(),
            _ => !string.IsNullOrWhiteSpace(Email));

        ResetPasswordCommand = new RelayCommand(
            async _ => await ResetPasswordAsync(),
            _ => !string.IsNullOrWhiteSpace(Email)
                 && !string.IsNullOrWhiteSpace(Token)
                 && !string.IsNullOrWhiteSpace(NewPassword));

        BackCommand = new RelayCommand(_ => nav.ShowLogin());
    }

    #region Bindable Properties 

    public string Email { get => _email; set => SetProperty(ref _email, value); }
    private string _email = string.Empty;

    public string Token { get => _token; set => SetProperty(ref _token, value); }
    private string _token = string.Empty;

    public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }
    private string _newPassword = string.Empty;

    #endregion

    #region Commands

    /// <summary>
    /// Requests a password reset token for the entered email address.
    /// </summary>
    public RelayCommand RequestTokenCommand { get; }

    /// <summary>
    /// Resets the user's password using the provided token.
    /// </summary>
    public RelayCommand ResetPasswordCommand { get; }

    /// <summary>
    /// Returns to the login screen.
    /// </summary>
    public RelayCommand BackCommand { get; }

    #endregion

    private async Task RequestTokenAsync()
    {
        NotificationService.Instance.Info("Requesting access token...");

        var result = await _api.ForgotPasswordAsync(new ForgotPasswordRequest { Email = Email.Trim() });
        if (!result.Success)
        {
            NotificationService.Instance.Error(result.Error ?? "Request failed.");
            return;
        }
        NotificationService.Instance.Info("If that email exists, a password reset token has been sent.");
    }

    private async Task ResetPasswordAsync()
    {
        NotificationService.Instance.Info("Resetting password...");

        var result = await _api.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = Email.Trim(),
            Token = Token.Trim(),
            NewPassword = NewPassword
        });

        if (!result.Success)
        {
            NotificationService.Instance.Error(result.Error ?? "Reset failed.");
            return;
        }

        NotificationService.Instance.Info("Password reset. You can log in now.");
        _nav.ShowLogin();
        Token = string.Empty;
        NewPassword = string.Empty;
    }
}