using NAudio.Wave;
using Ognia.Wpf.Configuration;
using Ognia.Wpf.Enums;
using Ognia.Wpf.Session;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;

namespace Ognia.Wpf.ViewModels.Shell;

public class PlaybackViewModel : ViewModelBase, IDisposable
{
    private const int WasapiLatencyMs = 500;

    private IWavePlayer? _output;
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
    private volatile bool _isTearingDownOutput;
    private readonly SemaphoreSlim _outputGate = new(1, 1);

    public PlaybackViewModel()
    {
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += OnTimerTick;
        _positionTimer.Start();

        var saved = AppSettings.Instance.Playback;
        _volume = Math.Clamp(saved.Volume, 0.0, 1.0);
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

    private async Task<IWavePlayer?> StartOutputAsync(AudioFileReader reader)
    {
        StopOutput();

        try
        {
            var output = await new WasapiPlayerBuilder()
                .WithDefaultDeviceStreamRouting()
                .WithLatency(WasapiLatencyMs)
                .WithMmcssThreadPriority("Audio")
                .BuildAsync();

            // A newer start replaced the reader while we awaited BuildAsync.
            if (!ReferenceEquals(reader, _reader))
            {
                try { output.Dispose(); } catch { }
                return null;
            }

            output.PlaybackStopped += OnOutputPlaybackStopped;
            output.Init(reader);
            _output = output;
            return output;
        }
        catch
        {
            if (!ReferenceEquals(reader, _reader))
                return null;

            var fallback = new WaveOut
            {
                BufferMilliseconds = WasapiLatencyMs,
                NumberOfBuffers = 3
            };
            fallback.PlaybackStopped += OnOutputPlaybackStopped;
            fallback.Init(reader);
            _output = fallback;
            return fallback;
        }
    }

    private void StopOutput()
    {
        var output = _output;
        if (output == null) return;

        _output = null;
        _isTearingDownOutput = true;
        try
        {
            try { output.PlaybackStopped -= OnOutputPlaybackStopped; } catch { }
            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { }
        }
        finally
        {
            _isTearingDownOutput = false;
        }
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
            PlaybackSessionState.Instance.Save();

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

    public PlaybackState PlaybackState => (_output?.PlaybackState ?? PlaybackState.Stopped);
    public bool IsPlaying => PlaybackState == PlaybackState.Playing;
    public bool IsPaused => PlaybackState == PlaybackState.Paused;
    public string PlayPauseLabel => IsPlaying ? "| |" : "▶";

    #endregion

    #region Position / duration

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            if (!SetProperty(ref _currentPosition, value)) return;
            OnPropertyChanged(nameof(PositionLabel));

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
                ApplyVolume();

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

    private void ApplyVolume()
    {
        // Route through the device's volume, not the reader, so the level
        // survives reader swaps and matches WASAPI's session volume semantics.
        if (_output is WasapiPlayer wasapi)
        {
            try { wasapi.Volume = (float)_volume; } catch { }
        }
        else if (_output is WaveOut wave)
        {
            try { wave.Volume = (float)_volume; } catch { }
        }
        else if (_reader != null)
        {
            // Fallback for any other IWavePlayer (kept for safety).
            try { _reader.Volume = (float)_volume; } catch { }
        }
    }

    private AudioFileReader CreateReader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var stream = new MemoryStream(bytes, writable: false);

        try
        {
            return new AudioFileReader(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private void DisposeReader()
    {
        try { _reader?.Dispose(); } catch { }
        _reader = null;
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

    public void PlayPause()
    {
        switch (PlaybackState)
        {
            case PlaybackState.Playing:
                _output?.Pause();
                PersistCurrentPosition();
                NotifyPlaybackChanged();
                return;

            case PlaybackState.Paused:
                _output?.Play();
                NotifyPlaybackChanged();
                return;
        }

        if (CurrentTrack == null || _isStarting) return;

        _isStarting = true;
        _ = StartPlayAsync(CurrentTrack);
    }

    private async Task StartPlayAsync(TrackRowViewModel track)
    {
        try { await PlayTrackAsync(track); }
        finally { _isStarting = false; }
    }

    public void Stop()
    {
        PersistCurrentPosition();

        StopOutput();
        DisposeReader();
        CurrentPosition = TimeSpan.Zero;
        TotalDuration = TimeSpan.Zero;
        NowPlaying = string.Empty;
        SetPreviewThumbnail(null);
        NotifyPlaybackChanged();
    }

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

        await StartCurrentTrackAsync();
    }

    private async Task StartCurrentTrackAsync()
    {
        if (CurrentTrack == null) return;

        var path = CurrentTrack.LocalFilePath;
        if (path == null)
        {
            ErrorOccurred?.Invoke($"No local file for '{CurrentTrack.Name}'.");
            Stop();
            return;
        }

        await _outputGate.WaitAsync();
        try
        {
            DisposeReader();
            _reader = CreateReader(path);

            var output = await StartOutputAsync(_reader);
            if (output == null) return;   // superseded by a newer start

            ApplyVolume();
            TotalDuration = _reader.TotalTime;

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

            output.Play();
            NowPlaying = CurrentTrack.Name;
            SetPreviewThumbnail(null);
            NotifyPlaybackChanged();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Playback failed for '{CurrentTrack?.Name}': {ex.Message}");
            Stop();
        }
        finally
        {
            _outputGate.Release();
        }
    }

    public string? NowPlayingSourceUrl => CurrentTrack?.SourceUrl ?? _previewSourceUrl;
    public string? NowPlayingThumbnail => CurrentTrack?.ThumbnailPath ?? _previewThumbnail;

    private string? _previewThumbnail;
    private string? _previewSourceUrl;

    public async Task PlayLocalFileAsync(
        string path,
        string displayName,
        string? thumbnailPath = null,
        string? sourceUrl = null)
    {
        await _outputGate.WaitAsync();
        try
        {
            CurrentTrack = null;

            DisposeReader();
            _reader = CreateReader(path);

            var output = await StartOutputAsync(_reader);
            if (output == null) return;

            ApplyVolume();
            TotalDuration = _reader.TotalTime;
            CurrentPosition = TimeSpan.Zero;
            output.Play();
            NowPlaying = displayName;
            SetPreviewThumbnail(thumbnailPath, sourceUrl);
            NotifyPlaybackChanged();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Preview failed: {ex.Message}");
        }
        finally
        {
            _outputGate.Release();
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

    private async Task NextAsync()
    {
        var next = GetManualNextTrack();
        if (next == null) return;
        await PlayTrackAsync(next);
    }

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

    private bool CanGoPrevious()
    {
        if (CurrentTrack == null) return false;
        var index = Tracks.IndexOf(CurrentTrack);
        if (index < 0) return false;
        return index > 0 || (RepeatMode == RepeatMode.All && Tracks.Count > 0);
    }

    private TrackRowViewModel? GetAutoAdvanceTrack()
    {
        if (CurrentTrack == null || Tracks.Count == 0) return null;
        if (RepeatMode == RepeatMode.One) return CurrentTrack;
        return GetSequentialOrShuffleNext();
    }

    private TrackRowViewModel? GetManualNextTrack()
    {
        if (CurrentTrack == null || Tracks.Count == 0) return null;
        return GetSequentialOrShuffleNext();
    }

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

    public void RestoreTrack(TrackRowViewModel track)
    {
        _pendingRestorePosition = TimeSpan.FromSeconds(
            Math.Max(0, PlaybackSessionState.Instance.LastTrackPosition));

        CurrentTrack = track;

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

    public void BeginSeek()
    {
        _isSeeking = true;
        _positionTimer.Stop();
    }

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

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isSeeking || _isAdvancing) return;

        if (_reader != null && PlaybackState == PlaybackState.Playing)
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

    private void NotifyPlaybackChanged()
    {
        OnPropertyChanged(nameof(PlaybackState));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PlayPauseLabel));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// WASAPI fires this on device loss (headphones unplugged), exclusive-mode
    /// grab, or driver failure — with <see cref="StoppedEventArgs.Exception"/> set.
    /// WaveOutEvent fires it only on explicit Stop/end-of-stream, exception null.
    /// </summary>
    private void OnOutputPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_disposed || _isTearingDownOutput || e.Exception == null) return;

        var text = (e.Exception.HResult == unchecked((int)0x80131622))
            ? $"Cannot play '{NowPlaying}': unsupported audio format."
            : $"Audio device error: {e.Exception.Message}";

        ErrorOccurred?.Invoke(text);
        NotifyPlaybackChanged();
    }

    private void PersistCurrentPosition()
    {
        if (_currentTrack == null) return;

        PlaybackSessionState.Instance.LastTrackPosition = CurrentPosition.TotalSeconds;
        AppSettings.Instance.Save();
        PlaybackSessionState.Instance.Save();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        PersistCurrentPosition();

        _positionTimer.Stop();
        _positionTimer.Tick -= OnTimerTick;

        StopOutput();
        DisposeReader();
        _outputGate.Dispose();
    }

    #endregion
}