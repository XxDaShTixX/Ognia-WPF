using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Session;

namespace Ognia.Wpf.ViewModels;

/// <summary>
/// Right-pane data context shown when more than one playlist track is checked.
/// Exposes only bulk actions. Target playlist for Copy/Move is picked per action.
/// </summary>
public sealed class MultiTrackSelectionViewModel : ViewModelBase
{
    private readonly AppSession _session;

    private PlaylistResponse? _copyTarget;
    private PlaylistResponse? _moveTarget;

    public MultiTrackSelectionViewModel(AppSession session)
    {
        _session = session;

        RemoveCommand = new RelayCommand(
            async _ => await RemoveAsync(),
            _ => _session.SelectedTrackCount > 0 && !_session.IsBusy);

        DownloadCommand = new RelayCommand(
            async _ => await _session.DownloadSelectedTracksAsync(),
            _ => _session.SelectedTracks.Any(t => !t.IsDownloaded) && !_session.IsBusy);

        RemoveDownloadCommand = new RelayCommand(
            _ => _session.RemoveDownloadsForSelected(),
            _ => _session.SelectedTracks.Any(t => t.IsDownloaded) && !_session.IsBusy);

        CopyCommand = new RelayCommand(
            async _ => await CopyAsync(),
            _ => CopyTarget != null && !_session.IsBusy);

        MoveCommand = new RelayCommand(
            async _ => await MoveAsync(),
            _ => MoveTarget != null && !_session.IsBusy);

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSession.SelectedTrackCount)
                or nameof(AppSession.HasMultiSelection))
            {
                OnPropertyChangedPublic();
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        };
    }

    public AppSession Session => _session;

    public int Count => _session.SelectedTrackCount;
    public string Header => $"{Count} tracks selected";

    /// <summary>Candidate targets for copy/move: every playlist except the current one.</summary>
    public IEnumerable<PlaylistResponse> PlaylistTargets =>
        _session.Playlists.Where(p => p.Id != _session.SelectedPlaylist?.Id);

    public PlaylistResponse? CopyTarget
    {
        get => _copyTarget;
        set { if (SetProperty(ref _copyTarget, value)) Requery(); }
    }

    public PlaylistResponse? MoveTarget
    {
        get => _moveTarget;
        set { if (SetProperty(ref _moveTarget, value)) Requery(); }
    }

    public RelayCommand RemoveCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand RemoveDownloadCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand MoveCommand { get; }

    public void OnPropertyChangedPublic()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(PlaylistTargets));
    }

    private async Task CopyAsync()
    {
        if (CopyTarget == null) return;
        await _session.CopySelectedTracksAsync(CopyTarget);
    }

    private async Task RemoveAsync()
    {
        var playlist = _session.SelectedPlaylist;
        var count = _session.SelectedTrackCount;
        if (playlist == null || count == 0) return;

        if (System.Windows.MessageBox.Show(
                $"Remove {count} selected track(s) from \"{playlist.Name}\"?",
                "Remove tracks",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        await _session.RemoveSelectedTracksAsync();
    }

    private async Task MoveAsync()
    {
        if (MoveTarget == null) return;
        await _session.MoveSelectedTracksAsync(MoveTarget);
    }

    private void Requery() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

}