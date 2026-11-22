using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Track;

namespace Ognia.Wpf.ViewModels;

public class TrackRowViewModel : ViewModelBase
{
    public TrackRowViewModel(
        TrackResponse track, string playlistName,
        Action<TrackRowViewModel> playRequested,
        Action<TrackRowViewModel> moreRequested,
        Action<TrackRowViewModel> renameRequested,
        Action<TrackRowViewModel> removeRequested,
        Action<TrackRowViewModel> downloadRequested,
        Action<TrackRowViewModel> removeDownloadRequested)
    {
        Track = track;
        _playlistName = playlistName;
        _playRequested = playRequested;
        _moreRequested = moreRequested;

        PlayCommand = new RelayCommand(_ => playRequested(this));
        MoreCommand = new RelayCommand(_ => moreRequested(this));
        RenameCommand = new RelayCommand(_ => renameRequested(this));
        RemoveCommand = new RelayCommand(_ => removeRequested(this));
        DownloadCommand = new RelayCommand(_ => downloadRequested(this));
        RemoveDownloadCommand = new RelayCommand(_ => removeDownloadRequested(this));

        _isDownloaded = LocalLibrary.FindLocalFile(playlistName, Name) != null;

        _ = LoadThumbnailAsync();
    }

    public RelayCommand PlayCommand { get; }
    public RelayCommand MoreCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand RemoveDownloadCommand { get; }

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

    private bool _isDownloaded;
    private bool _isPlaying;
    private string? _thumbnailPath;

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
}