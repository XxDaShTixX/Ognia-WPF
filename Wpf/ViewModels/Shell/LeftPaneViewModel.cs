using Ognia.Wpf.Authentication;
using Ognia.Wpf.Configuration;
using Ognia.Wpf.Enums;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Session;

namespace Ognia.Wpf.ViewModels.Shell;

public sealed class LeftPaneViewModel : ViewModelBase
{
    private readonly IShellNavigator _nav;

    public LeftPaneViewModel(AppSession session, IShellNavigator nav)
    {
        _nav = nav;
        Session = session;

        LibraryCommand = new RelayCommand(_ => _nav.ShowLibrary());
        SearchCommand = new RelayCommand(_ => _nav.ShowSearch());
        SettingsCommand = new RelayCommand(_ => _nav.ShowSettings());
    }

    public AppSession Session { get; }

    public RelayCommand LibraryCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand SettingsCommand { get; }

    public string WelcomeText
    {
        get
        {
            var who = AppSettings.Instance.Mode == AppMode.Guest
                ? "Guest"
                : TokenStore.Email ?? "Guest";
            return $"Welcome, {who}";
        }
    }
}