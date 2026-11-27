using Ognia.Wpf.Models.Track;
using Ognia.Wpf.Session;
using Ognia.Wpf.TrackSources;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Ognia.Wpf.ViewModels;

/// <summary>
/// A single search-result row. Owns its own per-row playlist dropdown
/// (bound to the session's playlists) and its add-to-playlist state.
/// Add-button enablement is resolved against the target playlist's
/// track list via the repo, cached per (row, playlistId).
/// Match is by (SourceName, SourceId) — <see cref="TrackResponse.Id"/> is
/// not stable across search results and playlists.
/// </summary>
public sealed class ResultRowViewModel : ViewModelBase
{
    private readonly AppSession _session;

    public TrackResponse Track { get; }
    public string Name => Track.Name;
    public string ThumbnailUrl => Track.ThumbnailUrl;
    public string SourceUrl => TrackSourceUrlResolver.BuildUrl(Track.SourceName, Track.SourceId) ?? string.Empty;
    public TimeSpan Duration => Track.Duration;
    public string DurationLabel => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}:{Duration.Minutes:00}:{Duration.Seconds:00}"
        : Duration.ToString(@"mm\:ss");

    public ObservableCollection<PlaylistMembershipViewModel> ExistingPlaylists { get; } = new();
    public ObservableCollection<PlaylistMembershipViewModel> OtherPlaylists { get; } = new();
    public ObservableCollection<PlaylistMembershipViewModel> Playlists { get; } = new();

    private Task? _loadMembershipTask;

    public ResultRowViewModel(TrackResponse track, AppSession session)
    {
        Track = track;
        _session = session;

        OpenPlaylistPickerCommand = new RelayCommand(async _ => await OpenPlaylistPickerAsync());
        TogglePlaylistCommand = new RelayCommand(async p =>
        {
            if (p is PlaylistMembershipViewModel playlist)
                await TogglePlaylistAsync(playlist);
        });

        session.Playlists.CollectionChanged += OnPlaylistsChanged;
        if (session.Playlists.Count > 0)
            _loadMembershipTask = LoadMembershipAsync();
    }

    public RelayCommand OpenPlaylistPickerCommand { get; }
    public RelayCommand TogglePlaylistCommand { get; }

    #region Bindable Properties

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }
    private bool _isPlaying;

    public bool IsPlaylistPickerOpen
    {
        get => _isPlaylistPickerOpen;
        set => SetProperty(ref _isPlaylistPickerOpen, value);
    }
    private bool _isPlaylistPickerOpen;

    public bool IsLoadingPlaylists
    {
        get => _isLoadingPlaylists;
        private set => SetProperty(ref _isLoadingPlaylists, value);
    }
    private bool _isLoadingPlaylists;

    public bool HasTrackInAnyPlaylist => ExistingPlaylists.Count > 0;
    public string PlaylistButtonGlyph => HasTrackInAnyPlaylist ? "✓" : "...";

    #endregion

    /// <summary>
    /// Picks the first playlist once the session's collection populates,
    /// then resolves the add-button state for it.
    /// </summary>
    private void OnPlaylistsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_loadMembershipTask != null || _session.Playlists.Count == 0) return;
        _loadMembershipTask = LoadMembershipAsync();
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
                    ? result.Data?.Tracks.FirstOrDefault(t => IsSameTrack(t))
                    : null;
                return new PlaylistMembershipViewModel(playlist, track?.Id);
            }));

            ExistingPlaylists.Clear();
            OtherPlaylists.Clear();
            Playlists.Clear();
            foreach (var membership in memberships.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                Playlists.Add(membership);
                AddSorted(membership.IsInPlaylist ? ExistingPlaylists : OtherPlaylists, membership);
            }

            OnPropertyChanged(nameof(HasTrackInAnyPlaylist));
            OnPropertyChanged(nameof(PlaylistButtonGlyph));
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
                var result = await _session.Repo.RemoveTrackAsync(membership.Playlist.Id, membership.TrackId.Value);
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
                var result = await _session.Repo.AddTracksAsync(membership.Playlist.Id, SourceUrl);
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

            OnPropertyChanged(nameof(HasTrackInAnyPlaylist));
            OnPropertyChanged(nameof(PlaylistButtonGlyph));
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
        {
            index++;
        }

        playlists.Insert(index, membership);
    }

    private bool IsSameTrack(TrackResponse track) =>
        string.Equals(track.SourceName, Track.SourceName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(track.SourceId, Track.SourceId, StringComparison.OrdinalIgnoreCase);
}

public sealed class PlaylistMembershipViewModel : ViewModelBase
{
    public PlaylistMembershipViewModel(Ognia.Wpf.Models.Playlist.PlaylistResponse playlist, int? trackId)
    {
        Playlist = playlist;
        TrackId = trackId;
        _isInPlaylist = trackId.HasValue;
    }

    public Ognia.Wpf.Models.Playlist.PlaylistResponse Playlist { get; }
    public int? TrackId { get; private set; }
    public string Name => Playlist.Name;
    public bool IsInPlaylist => _isInPlaylist;
    private bool _isInPlaylist;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }
    private bool _isBusy;

    public void SetPresent(int? trackId)
    {
        TrackId = trackId;
        _isInPlaylist = true;
        OnPropertyChanged(nameof(IsInPlaylist));
    }

    public void SetAbsent()
    {
        TrackId = null;
        _isInPlaylist = false;
        OnPropertyChanged(nameof(IsInPlaylist));
    }

    public void NotifyStateChanged() => OnPropertyChanged(nameof(IsInPlaylist));
}