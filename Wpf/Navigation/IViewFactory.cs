using Ognia.Wpf.Services;
using Ognia.Wpf.Session;
using Ognia.Wpf.Views.MainPane;

namespace Ognia.Wpf.Navigation;

public interface IViewFactory
{
    LibraryView CreateLibraryView(AppSession session, IShellNavigator navigator);

    PlaylistView CreatePlaylistView(AppSession session,
        IShellNavigator navigator);

    SearchView CreateSearchView(AppSession session);

    SettingsView CreateSettingsView(
        ApiClientService api,
        IAppNavigator navigator,
        AppSession session);
}