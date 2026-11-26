using Ognia.Wpf.Library;
using Ognia.Wpf.YouTube;
using System.Windows.Input;

namespace Ognia.Wpf.ViewModels.Shell;

public class DownloadViewModel : ViewModelBase
{
    // Per-track download coalescing. Two callers for the same track share one Task.
    private readonly Dictionary<int, Task<bool>> _inFlight = new();
    private readonly object _inFlightGate = new();

    private bool _isDownloading;
    private bool _isDownloadingAll;
    private int _downloadAllTotal;
    private int _downloadAllCompleted;

    public DownloadViewModel()
    {
        DownloadTrackCommand = new RelayCommand(
            async _ => await DownloadSelectedAsync(),
            _ => SelectedTrack != null && !IsDownloading);

        RemoveDownloadCommand = new RelayCommand(
            _ => RemoveSelectedDownload(),
            _ => SelectedTrack != null
                 && LocalLibrary.FindLocalFile(SelectedTrack.PlaylistName, SelectedTrack.Name) != null);

        DownloadAllCommand = new RelayCommand(
            async _ => await DownloadAllAsync(),
            _ => Tracks != null
                 && Tracks.Count > 0
                 && !IsDownloadingAll
                 && Tracks.Any(t => !t.IsDownloaded));

        RemoveAllDownloadsCommand = new RelayCommand(
            _ => RemoveAllDownloads(),
            _ => Tracks != null && Tracks.Any(t => t.IsDownloaded));
    }

    #region Owner-supplied state

    /// <summary>The selected track. Set by the owner when selection changes.</summary>
    public TrackRowViewModel? SelectedTrack { get; set; }

    /// <summary>The current playlist's tracks. Set by the owner when the playlist changes.</summary>
    public IReadOnlyList<TrackRowViewModel>? Tracks { get; set; }
    #endregion

    #region Commands

    public RelayCommand DownloadTrackCommand { get; }
    public RelayCommand RemoveDownloadCommand { get; }
    public RelayCommand DownloadAllCommand { get; }
    public RelayCommand RemoveAllDownloadsCommand { get; }

    #endregion

    #region Single-track state

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetProperty(ref _isDownloading, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    #endregion

    #region Batch state

    public bool IsDownloadingAll
    {
        get => _isDownloadingAll;
        set
        {
            if (SetProperty(ref _isDownloadingAll, value))
            {
                OnPropertyChanged(nameof(DownloadAllLabel));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public int DownloadAllTotal
    {
        get => _downloadAllTotal;
        set
        {
            if (SetProperty(ref _downloadAllTotal, value))
            {
                OnPropertyChanged(nameof(DownloadAllLabel));
                OnPropertyChanged(nameof(DownloadAllProgress));
            }
        }
    }

    public int DownloadAllCompleted
    {
        get => _downloadAllCompleted;
        set
        {
            if (SetProperty(ref _downloadAllCompleted, value))
            {
                OnPropertyChanged(nameof(DownloadAllLabel));
                OnPropertyChanged(nameof(DownloadAllProgress));
            }
        }
    }

    public string DownloadAllLabel =>
        IsDownloadingAll ? $"Downloading {DownloadAllCompleted}/{DownloadAllTotal}" : "";

    public double DownloadAllProgress =>
        DownloadAllTotal > 0 ? (double)DownloadAllCompleted / DownloadAllTotal * 100 : 0;

    #endregion

    #region Events

    /// <summary>Raised when a download operation fails, with a user-facing message.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Raised for every progress tick during a batch download; hosts route to the status bar.</summary>
    public event Action<string>? ProgressReported;

    #endregion

    #region Public API

    /// <summary>
    /// Ensures a track is downloaded. Returns true if the file is now local, false on failure.
    /// Concurrent calls for the same track share a single download.
    /// Used by PlaybackService via its TrackDownloadRequested callback.
    /// </summary>
    public Task<bool> EnsureDownloadedAsync(TrackRowViewModel track)
    {
        if (track.IsDownloaded) return Task.FromResult(true);

        lock (_inFlightGate)
        {
            if (_inFlight.TryGetValue(track.Id, out var existing))
                return existing;

            var task = DownloadAndFlagAsync(track);
            _inFlight[track.Id] = task;

            // Remove from the coalescing map once complete, success or failure.
            _ = task.ContinueWith(_ =>
            {
                lock (_inFlightGate) _inFlight.Remove(track.Id);
            }, TaskScheduler.Default);

            return task;
        }
    }

    private async Task<bool> DownloadAndFlagAsync(TrackRowViewModel track)
    {
        try
        {
            var downloader = new YouTubeAudioDownloader();
            await downloader.DownloadAsync(track);
            track.IsDownloaded = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Download failed for '{track.Name}': {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Command implementations

    /// <summary>Downloads the currently selected track.</summary>
    private async Task DownloadSelectedAsync()
    {
        if (SelectedTrack == null) return;

        IsDownloading = true;
        try
        {
            await EnsureDownloadedAsync(SelectedTrack);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>Deletes the local file for the currently selected track, if one exists.</summary>
    private void RemoveSelectedDownload()
    {
        if (SelectedTrack == null) return;

        var audioOk = LocalLibrary.DeleteLocalFile(SelectedTrack.PlaylistName, SelectedTrack.Name);
        LocalLibrary.DeleteLocalThumbnail(SelectedTrack.PlaylistName, SelectedTrack.Name);

        if (!audioOk)
        {
            ErrorOccurred?.Invoke("Could not delete the local file (it may have been moved or is locked).");
            return;
        }

        SelectedTrack.IsDownloaded = false;
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Downloads every undownloaded track in the current playlist, sequentially,
    /// reporting progress and counting failures without aborting the batch.
    /// </summary>
    private async Task DownloadAllAsync()
    {
        if (Tracks == null) return;

        var pending = Tracks.Where(t => !t.IsDownloaded).ToList();
        if (pending.Count == 0) return;

        IsDownloadingAll = true;
        DownloadAllTotal = pending.Count;
        DownloadAllCompleted = 0;

        ProgressReported?.Invoke($"Downloading 0/{pending.Count}…");

        var failed = 0;

        try
        {
            foreach (var track in pending)
            {
                if (track.IsDownloaded)
                {
                    DownloadAllCompleted++;
                    ProgressReported?.Invoke($"Downloading {DownloadAllCompleted}/{DownloadAllTotal}…");
                    continue;
                }

                ProgressReported?.Invoke($"Downloading {DownloadAllCompleted + 1}/{DownloadAllTotal}: {track.Name}");

                if (!await EnsureDownloadedAsync(track))
                    failed++;

                DownloadAllCompleted++;
                ProgressReported?.Invoke($"Downloading {DownloadAllCompleted}/{DownloadAllTotal}…");
            }
        }
        finally
        {
            IsDownloadingAll = false;
        }

        if (failed > 0)
        {
            ErrorOccurred?.Invoke($"{failed} track(s) failed to download.");
            ProgressReported?.Invoke($"{failed} track(s) failed to download.");
        }
        else
        {
            ProgressReported?.Invoke($"Downloaded {pending.Count} track(s).");
        }
    }

    /// <summary>Deletes every downloaded file for the current playlist and clears IsDownloaded on each row.</summary>
    private void RemoveAllDownloads()
    {
        if (Tracks == null) return;

        var downloaded = Tracks.Where(t => t.IsDownloaded).ToList();
        if (downloaded.Count == 0) return;

        if (System.Windows.MessageBox.Show(
                $"Delete {downloaded.Count} downloaded file(s) in this playlist?",
                "Remove all downloads",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        var failed = 0;

        foreach (var track in downloaded)
        {
            if (LocalLibrary.DeleteLocalFile(track.PlaylistName, track.Name))
                track.IsDownloaded = false;
            else
                failed++;

            LocalLibrary.DeleteLocalThumbnail(track.PlaylistName, track.Name);
        }

        CommandManager.InvalidateRequerySuggested();

        if (failed > 0)
            ErrorOccurred?.Invoke($"{failed} file(s) could not be deleted.");
    }

    #endregion
}