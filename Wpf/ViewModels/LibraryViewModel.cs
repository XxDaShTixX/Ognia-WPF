using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Services;
using Ognia.Wpf.Session;

namespace Ognia.Wpf.ViewModels;

public sealed class LibraryViewModel : ViewModelBase
{
    private readonly IShellNavigator _nav;

    public LibraryViewModel(AppSession session, IShellNavigator nav)
    {
        Session = session;
        _nav = nav;

        OpenPlaylistCommand = new RelayCommand(
            p => _nav.ShowPlaylist((PlaylistResponse)p!),
            p => p is PlaylistResponse);

        BeginCreatePlaylistCommand = new RelayCommand(_ => Session.BeginCreatePlaylist());

        ConfirmCreatePlaylistCommand = new RelayCommand(
            async _ => await ConfirmCreatePlaylistAsync(),
            _ => Session.IsCreatingPlaylist
                 && !string.IsNullOrWhiteSpace(Session.NewPlaylistName));

        CancelCreatePlaylistCommand = new RelayCommand(
            _ => Session.EndCreatePlaylist(),
            _ => Session.IsCreatingPlaylist);

        DeletePlaylistCommand = new RelayCommand(
            async p => await DeletePlaylistAsync(p as PlaylistResponse),
            p => p is PlaylistResponse);

        _ = Session.LoadPlaylistsAsync();
    }

    public AppSession Session { get; }

    public RelayCommand OpenPlaylistCommand { get; }
    public RelayCommand BeginCreatePlaylistCommand { get; }
    public RelayCommand ConfirmCreatePlaylistCommand { get; }
    public RelayCommand CancelCreatePlaylistCommand { get; }
    public RelayCommand DeletePlaylistCommand { get; }

    #region Create playlist
    private async Task ConfirmCreatePlaylistAsync()
    {
        var name = Session.NewPlaylistName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Session.EndCreatePlaylist();
            return;
        }

        var result = await Session.Repo.CreatePlaylistAsync(
            new CreatePlaylistRequest { Name = name });

        if (!result.Success)
        {
            NotificationService.Instance.Error(result.Error ?? "Failed to create playlist.");
            return;
        }

        Session.EndCreatePlaylist();
        await Session.LoadPlaylistsAsync();
        NotificationService.Instance.Info($"Created playlist: {name}");
    }

    /// <summary>
    /// Moves <paramref name="dragged"/> to <paramref name="toIndex"/> in the
    /// playlists collection, then persists the new order.
    /// </summary>
    public void MovePlaylist(PlaylistResponse dragged, int toIndex)
    {
        var full = Session.Playlists;
        var fromIndex = full.IndexOf(dragged);
        if (fromIndex < 0) return;
        if (toIndex < 0 || toIndex >= full.Count || toIndex == fromIndex) return;

        full.Move(fromIndex, toIndex);
        Session.PersistPlaylistOrder();
    }
    #endregion

    #region Delete playlist

    private async Task DeletePlaylistAsync(PlaylistResponse? playlist)
    {
        if (playlist == null) return;
        await Session.DeletePlaylistAsync(playlist);
    }

    #endregion
}