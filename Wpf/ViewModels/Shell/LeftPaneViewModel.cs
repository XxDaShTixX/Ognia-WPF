using Ognia.Wpf.Navigation;
using Ognia.Wpf.Session;

namespace Ognia.Wpf.ViewModels.Shell;

public sealed class LeftPaneViewModel : ViewModelBase
{
    private readonly IShellNavigator _nav;
    public AppSession Session { get; }

    public LeftPaneViewModel(AppSession session, IShellNavigator nav)
    {
        // Reference
        _nav = nav;
        Session = session;

        // Initialize commands
        LibraryCommand = new RelayCommand(_ =>
        {
            CurrentPage = "Library";
            _nav.ShowLibrary();
        });

        SearchCommand = new RelayCommand(_ =>
        {
            CurrentPage = "Search";
            _nav.ShowSearch();
        });

        SettingsCommand = new RelayCommand(_ =>
        {
            CurrentPage = "Settings";
            _nav.ShowSettings();
        });
    }

    #region Commands

    public RelayCommand LibraryCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand SettingsCommand { get; }

    #endregion

    public bool IsLibrarySelected => CurrentPage == "Library";
    public bool IsSearchSelected => CurrentPage == "Search";
    public bool IsSettingsSelected => CurrentPage == "Settings";

    private string _currentPage = "Library";
    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsLibrarySelected));
                OnPropertyChanged(nameof(IsSearchSelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }
}