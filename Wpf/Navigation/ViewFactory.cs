using Ognia.Wpf.Services;
using Ognia.Wpf.Session;
using Ognia.Wpf.ViewModels.MainPane;
using Ognia.Wpf.Views.MainPane;

namespace Ognia.Wpf.Navigation;

public sealed class ViewFactory : IViewFactory
{
    public LibraryView CreateLibraryView(
        AppSession session,
        IShellNavigator navigator)
    {
        return new LibraryView
        {
            DataContext = new LibraryViewModel(session, navigator)
        };
    }

    public PlaylistView CreatePlaylistView(
        AppSession session,
        IShellNavigator navigator)
    {
        return new PlaylistView
        {
            DataContext = new PlaylistViewModel(session, navigator)
        };
    }

    public SearchView CreateSearchView(AppSession session)
    {
        return new SearchView
        {
            DataContext = new SearchViewModel(session)
        };
    }

    public SettingsView CreateSettingsView(
        ApiClientService api,
        IAppNavigator navigator,
        AppSession session)
    {
        return new SettingsView(api, navigator, session);
    }
}