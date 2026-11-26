using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Models.Track;
using Ognia.Wpf.Session;
using Ognia.Wpf.TrackSources;
using System.Collections.ObjectModel;

namespace Ognia.Wpf.ViewModels;

/// <summary>
/// A single search-result row. Owns its own per-row playlist dropdown
/// (bound to the session's playlists) and its add-to-playlist state.
/// </summary>
public sealed class ResultRowViewModel : ViewModelBase
{
    private readonly AppSession _session;

    private bool _isPlaying;
    private PlaylistResponse? _selectedPlaylist;

    public ResultRowViewModel(TrackResponse track, AppSession session)
    {
        Track = track;
        _session = session;

        Playlists = session.Playlists;
        _selectedPlaylist = Playlists.FirstOrDefault();
    }

    public TrackResponse Track { get; }

    public ObservableCollection<PlaylistResponse> Playlists { get; }

    public PlaylistResponse? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set => SetProperty(ref _selectedPlaylist, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public string Name => Track.Name;
    public TimeSpan Duration => Track.Duration;
    public string ThumbnailUrl => Track.ThumbnailUrl;
    public string SourceUrl => TrackSourceUrlResolver.BuildUrl(Track.SourceName, Track.SourceId) ?? string.Empty;

    public string DurationLabel => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}:{Duration.Minutes:00}:{Duration.Seconds:00}"
        : Duration.ToString(@"mm\:ss");
}