using Ognia.Wpf.Caching;
using Ognia.Wpf.Library;
using Ognia.Wpf.Models.Track;
using Ognia.Wpf.Session;
using Ognia.Wpf.TrackSources;
using System.Collections.ObjectModel;

namespace Ognia.Wpf.ViewModels;

public class TrackRowViewModel : ViewModelBase
{
    public TrackRowViewModel(
        TrackResponse track, string playlistName, AppSession session,
        Action<TrackRowViewModel> playRequested,
        Action<TrackRowViewModel> moreRequested,
        Action<TrackRowViewModel> renameRequested,
        Action<TrackRowViewModel> removeRequested,
        Action<TrackRowViewModel> downloadRequested,
        Action<TrackRowViewModel> removeDownloadRequested)
    {
        Track = track;
        _playlistName = playlistName;
        _session = session;
        _playRequested = playRequested;
        _moreRequested = moreRequested;

        PlayCommand = new RelayCommand(_ => playRequested(this));
        MoreCommand = new RelayCommand(_ => moreRequested(this));
        RenameCommand = new RelayCommand(_ => renameRequested(this));
        RemoveCommand = new RelayCommand(_ => removeRequested(this));
        DownloadCommand = new RelayCommand(_ => downloadRequested(this));
        RemoveDownloadCommand = new RelayCommand(_ => removeDownloadRequested(this));
        OpenPlaylistPickerCommand = new RelayCommand(async _ => await OpenPlaylistPickerAsync());
        TogglePlaylistCommand = new RelayCommand(async parameter =>
        {
            if (parameter is PlaylistMembershipViewModel membership)
                await TogglePlaylistAsync(membership);
        });

        _isDownloaded = LocalLibrary.FindLocalFile(playlistName, Name) != null;

        _ = LoadThumbnailAsync();
    }

    public RelayCommand PlayCommand { get; }
    public RelayCommand MoreCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand RemoveDownloadCommand { get; }
    public RelayCommand OpenPlaylistPickerCommand { get; }
    public RelayCommand TogglePlaylistCommand { get; }

    public ObservableCollection<PlaylistMembershipViewModel> ExistingPlaylists { get; } = new();
    public ObservableCollection<PlaylistMembershipViewModel> OtherPlaylists { get; } = new();

    public bool IsPlaylistPickerOpen
    {
        get => _isPlaylistPickerOpen;
        set => SetProperty(ref _isPlaylistPickerOpen, value);
    }

    public bool IsLoadingPlaylists
    {
        get => _isLoadingPlaylists;
        private set => SetProperty(ref _isLoadingPlaylists, value);
    }

    public TrackResponse Track { get; }
    public string? LocalFilePath => _previewFilePath ?? LocalLibrary.FindLocalFile(PlaylistName, Name);

    /// <summary>
    /// Overrides the local file path for one-shot previews. When set,
    /// <see cref="LocalFilePath"/> returns this instead of resolving via playlist folder.
    /// </summary>
    private string? _previewFilePath;
    public void MarkPreviewFile(string path)
    {
        _previewFilePath = path;
        OnPropertyChanged(nameof(LocalFilePath));
        OnPropertyChanged(nameof(IsDownloaded));
    }

    private string _playlistName;
    public string PlaylistName
    {
        get => _playlistName;
        private set
        {
            if (_playlistName == value) return;
            _playlistName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LocalFilePath));
        }
    }
    /// <summary>
    /// Called when the owning playlist is renamed so path resolution follows the folder.
    /// </summary>
    public void UpdatePlaylistName(string newName) => PlaylistName = newName;

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        private set => SetThumbnailPath(value);
    }

    public bool IsDownloaded
    {
        get => _previewFilePath != null || _isDownloaded;
        set
        {
            if (!SetProperty(ref _isDownloaded, value)) return;
            OnPropertyChanged(nameof(DownloadedLabel));

            if (value && string.IsNullOrEmpty(_thumbnailPath))
                _ = LoadThumbnailAsync();
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            SelectionChanged?.Invoke(this, value);
        }
    }

    /// <summary>Raised when <see cref="IsSelected"/> toggles. Wired by AppSession.</summary>
    public event Action<TrackRowViewModel, bool>? SelectionChanged;

    private int _displayOrder;
    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetProperty(ref _displayOrder, value);
    }

    public string DownloadedLabel => IsDownloaded ? "✓" : "";
    public int Id => Track.Id;
    public string Name => Track.Name;
    public TimeSpan Duration => Track.Duration;
    public string ThumbnailUrl => Track.ThumbnailUrl;
    public int Position => Track.Position;
    public string SourceId => Track.SourceId;
    public string? SourceUrl => TrackSourceUrlResolver.BuildUrl(Track.SourceName, Track.SourceId);

    private readonly Action<TrackRowViewModel> _playRequested;
    private readonly Action<TrackRowViewModel> _moreRequested;
    private readonly AppSession _session;

    private bool _isDownloaded;
    private bool _isPlaying;
    private string? _thumbnailPath;
    private bool _isPlaylistPickerOpen;
    private bool _isLoadingPlaylists;
    private Task? _loadMembershipTask;

    private async Task LoadThumbnailAsync()
    {
        // First attempt: may return null if the file is still being written.
        var cached = await ThumbnailCache.GetOrDownloadAsync(
            Track.ThumbnailUrl, PlaylistName, Name);

        if (cached != null)
        {
            SetThumbnailPath(cached);
            return;
        }

        // Fallback to remote while retrying.
        if (!string.IsNullOrWhiteSpace(Track.ThumbnailUrl))
            SetThumbnailPath(Track.ThumbnailUrl);

        // Retry a few times — covers the race with the playlist add/download.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(500);
            cached = await ThumbnailCache.GetOrDownloadAsync(
                Track.ThumbnailUrl, PlaylistName, Name);

            if (cached != null)
            {
                SetThumbnailPath(cached);
                return;
            }
        }
    }

    private void SetThumbnailPath(string? value)
    {
        if (_thumbnailPath == value) return;
        _thumbnailPath = null;
        OnPropertyChanged(nameof(ThumbnailPath));
        _thumbnailPath = value;
        OnPropertyChanged(nameof(ThumbnailPath));
    }

    private async Task OpenPlaylistPickerAsync()
    {
        await EnsureMembershipLoadedAsync();
        IsPlaylistPickerOpen = true;
    }

    private async Task EnsureMembershipLoadedAsync()
    {
        _loadMembershipTask ??= LoadMembershipAsync();
        await _loadMembershipTask;
    }

    private async Task LoadMembershipAsync()
    {
        IsLoadingPlaylists = true;
        try
        {
            var memberships = await Task.WhenAll(_session.Playlists.Select(async playlist =>
            {
                var result = await _session.Repo.GetPlaylistDetailAsync(playlist.Id);
                var track = result.Success
                    ? result.Data?.Tracks.FirstOrDefault(IsSameTrack)
                    : null;
                return new PlaylistMembershipViewModel(playlist, track?.Id);
            }));

            ExistingPlaylists.Clear();
            OtherPlaylists.Clear();
            foreach (var membership in memberships.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                AddSorted(membership.IsInPlaylist ? ExistingPlaylists : OtherPlaylists, membership);
        }
        finally
        {
            IsLoadingPlaylists = false;
        }
    }

    private async Task TogglePlaylistAsync(PlaylistMembershipViewModel membership)
    {
        if (membership.IsBusy)
        {
            membership.NotifyStateChanged();
            return;
        }

        membership.IsBusy = true;
        try
        {
            if (membership.IsInPlaylist)
            {
                if (!membership.TrackId.HasValue) return;
                var result = await _session.Repo.RemoveTrackAsync(
                    membership.Playlist.Id, membership.TrackId.Value);
                if (!result.Success)
                {
                    Services.NotificationService.Instance.Error(result.Error ?? "Failed to remove track.");
                    membership.NotifyStateChanged();
                    return;
                }

                ExistingPlaylists.Remove(membership);
                membership.SetAbsent();
                AddSorted(OtherPlaylists, membership);
                Services.NotificationService.Instance.Info(
                    $"Removed '{Name}' from '{membership.Name}'.");
            }
            else
            {
                var result = await _session.Repo.AddTracksAsync(membership.Playlist.Id, SourceUrl ?? string.Empty);
                if (!result.Success)
                {
                    Services.NotificationService.Instance.Error(result.Error ?? "Failed to add track.");
                    membership.NotifyStateChanged();
                    return;
                }

                var detail = await _session.Repo.GetPlaylistDetailAsync(membership.Playlist.Id);
                OtherPlaylists.Remove(membership);
                membership.SetPresent(detail.Data?.Tracks.FirstOrDefault(IsSameTrack)?.Id);
                AddSorted(ExistingPlaylists, membership);
                Services.NotificationService.Instance.Info(
                    $"Added '{Name}' to '{membership.Name}'.");
            }
        }
        finally
        {
            membership.IsBusy = false;
        }
    }

    private static void AddSorted(
        ObservableCollection<PlaylistMembershipViewModel> playlists,
        PlaylistMembershipViewModel membership)
    {
        var index = 0;
        while (index < playlists.Count
               && StringComparer.OrdinalIgnoreCase.Compare(playlists[index].Name, membership.Name) <= 0)
            index++;

        playlists.Insert(index, membership);
    }

    private bool IsSameTrack(TrackResponse track) =>
        string.Equals(track.SourceName, Track.SourceName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(track.SourceId, Track.SourceId, StringComparison.OrdinalIgnoreCase);
}