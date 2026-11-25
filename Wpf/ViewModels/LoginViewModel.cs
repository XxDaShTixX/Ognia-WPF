using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Auth;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Repos;
using Ognia.Wpf.Services;

namespace Ognia.Wpf.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly ApiClientService _api;
    private readonly IAppNavigator _nav;

    public LoginViewModel(ApiClientService api, IAppNavigator nav)
    {
        _api = api;
        _nav = nav;

        LoginCommand = new RelayCommand(
            async _ => await LoginAsync(),
            _ => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password));

        SignUpCommand = new RelayCommand(
            async _ => await SignUpAsync(),
            _ => !string.IsNullOrWhiteSpace(RegEmail) && !string.IsNullOrWhiteSpace(RegPassword));

        GuestCommand = new RelayCommand(_ => _nav.EnterGuestMode());
        ForgotPasswordCommand = new RelayCommand(_ => _nav.ShowResetPassword());

        ShowLoginFormCommand = new RelayCommand(_ => ShowLoginForm = true);
        BackToLandingCommand = new RelayCommand(_ => ShowLoginForm = false);
    }

    #region Bindable Properties 

    /// <summary>
    /// Gets a value indicating whether the landing screen is visible.
    /// The landing screen is shown when the login form is hidden.
    /// </summary>
    public bool ShowLanding => !_showLoginForm;

    /// <summary>
    /// Gets or sets whether the login form is visible.
    /// When this value changes, dependent visibility properties are notified.
    /// </summary>
    public bool ShowLoginForm
    {
        get => _showLoginForm;
        set
        {
            if (!SetProperty(ref _showLoginForm, value)) return;
            OnPropertyChanged(nameof(ShowLanding));
        }
    }
    private bool _showLoginForm;

    /// <summary>
    /// Gets or sets the email address entered by the user.
    /// </summary>
    public string Email { get => _email; set => SetProperty(ref _email, value); }
    private string _email = string.Empty;

    /// <summary>
    /// Gets or sets the password used for login.
    /// </summary>
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    private string _password = string.Empty;

    /// <summary>
    /// Gets or sets the email address used for account registration.
    /// </summary>
    public string RegEmail { get => _regEmail; set => SetProperty(ref _regEmail, value); }
    private string _regEmail = string.Empty;

    /// <summary>
    /// Gets or sets the password used for account registration.
    /// </summary>
    public string RegPassword { get => _regPassword; set => SetProperty(ref _regPassword, value); }
    private string _regPassword = string.Empty;
    #endregion




    public RelayCommand LoginCommand { get; }
    public RelayCommand SignUpCommand { get; }
    public RelayCommand GuestCommand { get; }
    public RelayCommand ForgotPasswordCommand { get; }
    public RelayCommand ShowLoginFormCommand { get; }
    public RelayCommand BackToLandingCommand { get; }

    private async Task LoginAsync()
    {
        NotificationService.Instance.Info("Connecting...");

        var result = await _api.LoginAsync(new LoginRequest
        {
            Email = Email.Trim(),
            Password = Password
        });

        if (!result.Success || result.Data == null)
        {
            NotificationService.Instance.Error("Login failed.");
            return;
        }

        TokenStore.Set(result.Data.Token, result.Data.Email, result.Data.AccessTokenExpiresAt, result.Data.RefreshToken);

        if (_nav.HasPendingUpgrade)
            await _nav.CompleteUpgradeAsync();
        else
            _nav.ShowShell(new ApiMusicRepo(_api));
    }

    private async Task SignUpAsync()
    {
        NotificationService.Instance.Info("Connecting...");

        var result = await _api.RegisterAsync(new RegisterRequest
        {
            Email = RegEmail.Trim(),
            Password = RegPassword
        });

        if (!result.Success)
        {
            NotificationService.Instance.Info("Registration failed.");
            return;
        }

        NotificationService.Instance.Info("Account created. You can log in now.");
    }
}