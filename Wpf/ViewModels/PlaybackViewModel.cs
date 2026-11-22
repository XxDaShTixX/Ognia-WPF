using NAudio.Wave;
using Ognia.Wpf.Enums;
using Ognia.Wpf.Helpers;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;

namespace Ognia.Wpf.ViewModels;

public class PlaybackViewModel : ViewModelBase, IDisposable
{
    private readonly WaveOut _waveOut = new();
    private AudioFileReader? _reader;
    private readonly DispatcherTimer _positionTimer;
    private readonly Random _random = new();

    private TimeSpan _currentPosition;
    private TimeSpan _totalDuration;
    private TimeSpan _pendingRestorePosition;

    private bool _isStarting;
    private bool _isSeeking;
    private bool _isAdvancing;
    private string _nowPlaying = string.Empty;
    private TrackRowViewModel? _currentTrack;
    private bool _disposed;


    public PlaybackViewModel()
    {
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += OnTimerTick;
        _positionTimer.Start();

        // Restore last-used state.
        var saved = AppSettings.Instance.Playback;
        _volume = Math.Clamp(saved.Volume, 0.0, 1.0);
        _waveOut.Volume = (float)_volume;
        _repeatMode = saved.RepeatMode;
        _isShuffleOn = saved.Shuffle;

        PlayPauseCommand = new RelayCommand(_ => PlayPause(), _ => CurrentTrack != null);
        StopCommand = new RelayCommand(_ => Stop(), _ => PlaybackState != NAudio.Wave.PlaybackState.Stopped);
        NextCommand = new RelayCommand(async _ => await NextAsync(), _ => GetManualNextTrack() != null);
        PreviousCommand = new RelayCommand(async _ => await PreviousAsync(), _ => CanGoPrevious());
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());
        CycleRepeatCommand = new RelayCommand(_ => CycleRepeat());
        ToggleShuffleCommand = new RelayCommand(_ => IsShuffleOn = !IsShuffleOn);
    }

    #region Owner-supplied state

    /// <summary>The current playlist's tracks. Set by the owner when the playlist changes.</summary>
    public ObservableCollection<TrackRowViewModel> Tracks { get; set; } = new();

    /// <summary>
    /// Callback the owner wires to the download service.
    /// Returns true if the track is now local, false to abort playback.
    /// </summary>
    public Func<TrackRowViewModel, Task<bool>>? TrackDownloadRequested { get; set; }

    /// <summary>The track currently loaded into the player. Setting it refreshes command state.</summary>
    public TrackRowViewModel? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            if (_currentTrack == value) return;

            if (_currentTrack != null) _currentTrack.IsPlaying = false;
            _currentTrack = value;
            if (_currentTrack != null) _currentTrack.IsPlaying = true;

            PlaybackSessionState.Instance.LastTrackId = value?.Id;
            if (_pendingRestorePosition == TimeSpan.Zero)
                PlaybackSessionState.Instance.LastTrackPosition = 0;
            AppSettings.Instance.Save();

            OnPropertyChanged(nameof(CurrentTrack));
            OnPropertyChanged(nameof(NowPlayingThumbnail));
            OnPropertyChanged(nameof(NowPlayingSourceUrl));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    #endregion

    #region Events

    /// <summary>Raised when a playback operation fails, with a user-facing message.</summary>
    public event Action<string>? ErrorOccurred;

    #endregion

    #region Commands

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand CycleRepeatCommand { get; }
    public RelayCommand ToggleShuffleCommand { get; }

    #endregion

    #region Playback state

    public PlaybackState PlaybackState => _waveOut.PlaybackState;
    public bool IsPlaying => PlaybackState == PlaybackState.Playing;
    public bool IsPaused => PlaybackState == PlaybackState.Paused;
    public string PlayPauseLabel => IsPlaying ? "▐▐" : "▶";

    #endregion

    #region Position / duration

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            if (!SetProperty(ref _currentPosition, value)) return;
            OnPropertyChanged(nameof(PositionLabel));

            // Apply immediately when changed outside an active drag (e.g. click-to-seek).
            if (!_isSeeking && _reader != null)
            {
                var clamped = value < TimeSpan.Zero ? TimeSpan.Zero
                    : value > _reader.TotalTime ? _reader.TotalTime
                    : value;
                try { _reader.CurrentTime = clamped; } catch { }
            }
        }
    }

    public TimeSpan TotalDuration
    {
        get => _totalDuration;
        set
        {
            if (SetProperty(ref _totalDuration, value))
                OnPropertyChanged(nameof(DurationLabel));
        }
    }

    public string PositionLabel => FormatTime(CurrentPosition);
    public string DurationLabel => FormatTime(TotalDuration);

    private static string FormatTime(TimeSpan t) => t.TotalHours >= 1
        ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
        : t.ToString(@"mm\:ss");

    public string NowPlaying
    {
        get => _nowPlaying;
        set => SetProperty(ref _nowPlaying, value);
    }

    #endregion

    #region Volume / Mute

    private double _volumeBeforeMute = 1.0;

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                _waveOut.Volume = (float)value;
                if (value > 0 && _isMuted)
                {
                    _isMuted = false;
                    OnPropertyChanged(nameof(IsMuted));
                    OnPropertyChanged(nameof(VolumeIcon));
                }

                AppSettings.Instance.Playback.Volume = value;
                AppSettings.Instance.Save();
            }
        }
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
                OnPropertyChanged(nameof(VolumeIcon));
        }
    }

    public string VolumeIcon => IsMuted ? "🔇" : "🔊";

    /// <summary>Mutes the player, remembering the previous volume, or restores it when unmuting.</summary>
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            Volume = _volumeBeforeMute;
        }
        else
        {
            _volumeBeforeMute = Volume > 0 ? Volume : 1.0;
            IsMuted = true;
            Volume = 0;
        }
    }

    #endregion

    #region Repeat / shuffle
    public string RepeatLabel => RepeatMode switch
    {
        RepeatMode.Off => "➜",
        RepeatMode.All => "↳↰",
        RepeatMode.One => "⟲",
        _ => "➜"
    };

    private RepeatMode _repeatMode = RepeatMode.Off;
    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (SetProperty(ref _repeatMode, value))
            {
                OnPropertyChanged(nameof(RepeatLabel));
                CommandManager.InvalidateRequerySuggested();

                AppSettings.Instance.Playback.RepeatMode = value;
                AppSettings.Instance.Save();
            }
        }
    }

    private bool _isShuffleOn;
    public bool IsShuffleOn
    {
        get => _isShuffleOn;
        set
        {
            if (SetProperty(ref _isShuffleOn, value))
            {
                OnPropertyChanged(nameof(ShuffleLabel));
                CommandManager.InvalidateRequerySuggested();

                AppSettings.Instance.Playback.Shuffle = value;
                AppSettings.Instance.Save();
            }
        }
    }

    public string ShuffleLabel => IsShuffleOn ? "⇄" : "⊘";

    /// <summary>Cycles the repeat mode Off → All → One → Off.</summary>
    private void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off
        };
    }

    #endregion

    #region Transport: play / pause / stop

    /// <summary>
    /// Toggles pause/resume if a track is loaded, otherwise starts the current track
    /// (downloading it first if needed). Guards against double-start while preparing.
    /// </summary>
    public void PlayPause()
    {
        switch (_waveOut.PlaybackState)
        {
            case PlaybackState.Playing:
                _waveOut.Pause();
                PersistCurrentPosition();
                NotifyPlaybackChanged();
                return;

            case PlaybackState.Paused:
                _waveOut.Play();
                NotifyPlaybackChanged();
                return;
        }

        if (CurrentTrack == null || _isStarting) return;

        _isStarting = true;
        _ = StartPlayAsync(CurrentTrack);
    }

    /// <summary>Runs PlayTrackAsync while the _isStarting guard is held, then releases it.</summary>
    private async Task StartPlayAsync(TrackRowViewModel track)
    {
        try { await PlayTrackAsync(track); }
        finally { _isStarting = false; }
    }

    /// <summary>Stops playback, disposes the reader, and resets position, duration, and now-playing.</summary>
    public void Stop()
    {
        PersistCurrentPosition();

        _waveOut.Stop();
        _reader?.Dispose();
        _reader = null;
        CurrentPosition = TimeSpan.Zero;
        TotalDuration = TimeSpan.Zero;
        NowPlaying = string.Empty;
        SetPreviewThumbnail(null);
        NotifyPlaybackChanged();
    }

    /// <summary>
    /// Plays a specific track: downloads it first if not local, then starts playback.
    /// Aborts via Stop() if the download fails or no download callback is wired.
    /// </summary>
    public async Task PlayTrackAsync(TrackRowViewModel track)
    {
        CurrentTrack = track;

        if (!track.IsDownloaded)
        {
            NowPlaying = $"Preparing '{track.Name}'…";
            if (TrackDownloadRequested == null)
            {
                Stop();
                return;
            }

            var ok = await TrackDownloadRequested(track);
            if (!ok)
            {
                Stop();
                return;
            }
        }

        StartCurrentTrack();
    }

    /// <summary>
    /// Loads the current track's local file into NAudio and starts playback.
    /// Stops if the file cannot be found or the reader fails to initialize.
    /// </summary>
    private void StartCurrentTrack()
    {
        if (CurrentTrack == null) return;

        var path = CurrentTrack.LocalFilePath;
        if (path == null)
        {
            ErrorOccurred?.Invoke($"No local file for '{CurrentTrack.Name}'.");
            Stop();
            return;
        }

        try
        {
            _waveOut.Stop();
            _reader?.Dispose();
            _reader = new AudioFileReader(path);
            _waveOut.Init(_reader);
            TotalDuration = _reader.TotalTime;

            // Apply any pending restore position (clamped to file length).
            var startAt = _pendingRestorePosition;
            _pendingRestorePosition = TimeSpan.Zero;
            if (startAt > TimeSpan.Zero && startAt < _reader.TotalTime)
            {
                _reader.CurrentTime = startAt;
                CurrentPosition = startAt;
            }
            else
            {
                CurrentPosition = TimeSpan.Zero;
            }

            _waveOut.Play();
            NowPlaying = CurrentTrack.Name;
            SetPreviewThumbnail(null);
            NotifyPlaybackChanged();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Playback failed: {ex.Message}");
            Stop();
        }
    }


    /// <summary>
    /// The YouTube (or other source) URL for the currently playing item.
    /// Falls back to the preview URL when previewing from search results.
    /// </summary>
    public string? NowPlayingSourceUrl => CurrentTrack?.SourceUrl ?? _previewSourceUrl;
    public string? NowPlayingThumbnail => CurrentTrack?.ThumbnailPath ?? _previewThumbnail;
    
    private string? _previewThumbnail;
    private string? _previewSourceUrl;

    public void PlayLocalFile(
        string path, 
        string displayName,
        string? thumbnailPath = null, 
        string? sourceUrl = null)
    {
        try
        {
            CurrentTrack = null;

            _waveOut.Stop();
            _reader?.Dispose();
            _reader = new AudioFileReader(path);
            _waveOut.Init(_reader);
            TotalDuration = _reader.TotalTime;
            CurrentPosition = TimeSpan.Zero;
            _waveOut.Play();
            NowPlaying = displayName;
            SetPreviewThumbnail(thumbnailPath, sourceUrl);
            try
            {
                File.AppendAllText(
                Path.Combine(AppPaths.AppDataFolder, "preview.log"),
                $"{DateTime.Now:O} path={path} thumb={thumbnailPath ?? "<null>"} preview={_previewThumbnail ?? "<null>"} currentTrack={_currentTrack?.Name ?? "<null>"}\n");
            }
            catch { }
            NotifyPlaybackChanged();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Preview failed: {ex.Message}");
        }
    }

    private void SetPreviewThumbnail(string? path, string? sourceUrl = null)
    {
        _previewThumbnail = path;
        _previewSourceUrl = sourceUrl;
        OnPropertyChanged(nameof(NowPlayingThumbnail));
        OnPropertyChanged(nameof(NowPlayingSourceUrl));
    }

    #endregion

    #region Transport: next / previous

    /// <summary>Plays the next track according to shuffle/repeat rules (manual skip ignores Repeat One).</summary>
    private async Task NextAsync()
    {
        var next = GetManualNextTrack();
        if (next == null) return;
        await PlayTrackAsync(next);
    }

    /// <summary>Plays the previous track, wrapping to the last one when Repeat All is on.</summary>
    private async Task PreviousAsync()
    {
        if (CurrentTrack == null) return;

        var index = Tracks.IndexOf(CurrentTrack);
        if (index <= 0)
        {
            if (RepeatMode == RepeatMode.All && Tracks.Count > 0)
                await PlayTrackAsync(Tracks[^1]);
            return;
        }

        await PlayTrackAsync(Tracks[index - 1]);
    }

    /// <summary>Whether a previous track can be reached (sequentially, or by wrapping with Repeat All).</summary>
    private bool CanGoPrevious()
    {
        if (CurrentTrack == null) return false;
        var index = Tracks.IndexOf(CurrentTrack);
        if (index < 0) return false;
        return index > 0 || (RepeatMode == RepeatMode.All && Tracks.Count > 0);
    }

    /// <summary>Resolves the track auto-advance should play, honoring Repeat One.</summary>
    private TrackRowViewModel? GetAutoAdvanceTrack()
    {
        if (CurrentTrack == null || Tracks.Count == 0) return null;
        if (RepeatMode == RepeatMode.One) return CurrentTrack;
        return GetSequentialOrShuffleNext();
    }

    /// <summary>Resolves the track a manual Next should play, ignoring Repeat One.</summary>
    private TrackRowViewModel? GetManualNextTrack()
    {
        if (CurrentTrack == null || Tracks.Count == 0) return null;
        return GetSequentialOrShuffleNext();
    }

    /// <summary>
    /// Picks the next track: random (excluding current) when shuffle is on,
    /// otherwise the following track, wrapping to the first only with Repeat All.
    /// </summary>
    private TrackRowViewModel? GetSequentialOrShuffleNext()
    {
        if (CurrentTrack == null) return null;

        if (IsShuffleOn && Tracks.Count > 1)
        {
            var candidates = Tracks.Where(t => t != CurrentTrack).ToList();
            return candidates[_random.Next(candidates.Count)];
        }

        var index = Tracks.IndexOf(CurrentTrack);
        if (index < 0) return null;

        var nextIndex = index + 1;
        if (nextIndex >= Tracks.Count)
            return RepeatMode == RepeatMode.All ? Tracks[0] : null;

        return Tracks[nextIndex];
    }

    /// <summary>
    /// Re-resolves CurrentTrack to the matching instance in Tracks (matched by Id).
    /// If the track is not in the new collection, leaves CurrentTrack untouched —
    /// playback continues against the existing instance and the playback bar keeps rendering it.
    /// </summary>
    public void RebindCurrentTrack()
    {
        if (_currentTrack == null) return;

        var id = _currentTrack.Id;
        var match = Tracks.FirstOrDefault(t => t.Id == id);
        if (match == null) return;

        if (ReferenceEquals(match, _currentTrack)) return;

        _currentTrack.IsPlaying = false;
        match.IsPlaying = true;
        _currentTrack = match;
        OnPropertyChanged(nameof(CurrentTrack));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Sets the current track without playing, downloading, or touching <see cref="Tracks"/>.
    /// Used on startup to restore the playback bar from persisted state.
    /// </summary>
    public void RestoreTrack(TrackRowViewModel track)
    {
        _pendingRestorePosition = TimeSpan.FromSeconds(
            Math.Max(0, PlaybackSessionState.Instance.LastTrackPosition));

        CurrentTrack = track;

        // Give the seek slider a range before the reader exists, otherwise
        // Maximum (bound to TotalDuration) is 0 and the thumb pins to the end.
        TotalDuration = track.Duration;

        if (_pendingRestorePosition > TimeSpan.Zero && _pendingRestorePosition < track.Duration)
            CurrentPosition = _pendingRestorePosition;

        NowPlaying = track.Name;
        OnPropertyChanged(nameof(CurrentTrack));
        OnPropertyChanged(nameof(NowPlaying));
        CommandManager.InvalidateRequerySuggested();
    }

    #endregion

    #region Seeking

    /// <summary>Pauses the position timer while the user drags the seek slider.</summary>
    public void BeginSeek()
    {
        _isSeeking = true;
        _positionTimer.Stop();
    }

    /// <summary>Applies the sought position to the reader, then restarts the timer.</summary>
    public void EndSeek(TimeSpan position)
    {
        if (_reader != null)
        {
            _reader.CurrentTime = position;
            CurrentPosition = position;
            PersistCurrentPosition();
        }

        _isSeeking = false;
        _positionTimer.Start();
    }

    #endregion

    #region Timer

    /// <summary>
    /// Polls playback position every 500ms; on track end, advances to the next track
    /// (honoring repeat/shuffle) or stops when the playlist is exhausted.
    /// </summary>
    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isSeeking || _isAdvancing) return;

        if (_reader != null && _waveOut.PlaybackState == NAudio.Wave.PlaybackState.Playing)
            CurrentPosition = _reader.CurrentTime;

        if (TotalDuration > TimeSpan.Zero && CurrentPosition >= TotalDuration - TimeSpan.FromSeconds(0.5))
        {
            CurrentPosition = TimeSpan.Zero;
            TotalDuration = TimeSpan.Zero;

            var next = GetAutoAdvanceTrack();
            if (next != null)
            {
                _isAdvancing = true;
                try { await PlayTrackAsync(next); }
                finally { _isAdvancing = false; }
            }
            else
            {
                Stop();
            }
        }
    }

    #endregion

    #region Notifications

    /// <summary>Raises change notifications for all playback state properties and re-queries commands.</summary>
    private void NotifyPlaybackChanged()
    {
        OnPropertyChanged(nameof(PlaybackState));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PlayPauseLabel));
        CommandManager.InvalidateRequerySuggested();
    }

    private void PersistCurrentPosition()
    {
        if (_currentTrack == null) return;

        PlaybackSessionState.Instance.LastTrackPosition = CurrentPosition.TotalSeconds;
        AppSettings.Instance.Save();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        PersistCurrentPosition();

        _positionTimer.Stop();
        _positionTimer.Tick -= OnTimerTick;

        try { _waveOut.Stop(); } catch { }

        _reader?.Dispose();
        _reader = null;

        _waveOut.Dispose();
    }

    #endregion
}