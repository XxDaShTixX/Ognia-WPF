using Ognia.Wpf.Local;
using Ognia.Wpf.Session;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Ognia.Wpf.ViewModels;

public sealed class SearchViewModel : ViewModelBase
{
    private readonly YouTubeSearch _search = new();

    private string _query = string.Empty;
    private string _status = string.Empty;
    private int _page = 1;
    private bool _isBusy;
    private int _pageSize = 20;

    public SearchViewModel(AppSession session)
    {
        Session = session;

        SearchCommand = new RelayCommand(
            async _ => await RunSearchAsync(),
            _ => !IsBusy && !string.IsNullOrWhiteSpace(Query));

        NextPageCommand = new RelayCommand(
            async _ => await GoToPageAsync(Page + 1),
            _ => !IsBusy && HasNextPage);

        PrevPageCommand = new RelayCommand(
            async _ => await GoToPageAsync(Page - 1),
            _ => !IsBusy && Page > 1);

        PreviewCommand = new RelayCommand(
            async p => await PreviewAsync((ResultRowViewModel)p!),
            p => p is ResultRowViewModel);

        AddToPlaylistCommand = new RelayCommand(
            async p => await AddToPlaylistAsync((ResultRowViewModel)p!),
            p => p is ResultRowViewModel r && r.SelectedPlaylist != null);

        PageSizeOptions = new[] { 10, 20, 30, 50 };
    }

    public AppSession Session { get; }

    public ObservableCollection<ResultRowViewModel> Results { get; } = new();

    public IReadOnlyList<int> PageSizeOptions { get; }

    #region State

    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public int Page
    {
        get => _page;
        private set
        {
            if (!SetProperty(ref _page, value)) return;
            OnPropertyChanged(nameof(PageLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value <= 0) return;
            if (!SetProperty(ref _pageSize, value)) return;
            OnPropertyChanged(nameof(PageLabel));
            _ = GoToPageAsync(1);
        }
    }

    public bool HasNextPage => Results.Count == PageSize;

    public string PageLabel => $"Page {Page}";

    #endregion

    #region Commands

    public RelayCommand SearchCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand PrevPageCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand AddToPlaylistCommand { get; }

    #endregion

    #region Search pipeline

    private async Task RunSearchAsync()
    {
        Page = 1;
        await LoadPageAsync();
    }

    private async Task GoToPageAsync(int page)
    {
        if (page < 1) return;
        Page = page;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return;

        IsBusy = true;
        Status = "Searching…";
        Results.Clear();

        try
        {
            var offset = (Page - 1) * PageSize;
            var items = await _search.SearchAsync(Query.Trim(), offset, PageSize);

            foreach (var t in items)
                Results.Add(new ResultRowViewModel(t, Session));

            OnPropertyChanged(nameof(HasNextPage));
            Status = items.Count == 0
                ? "No results."
                : $"{items.Count} result(s) — page {Page}";
        }
        catch (Exception ex)
        {
            Status = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Row actions

    private async Task PreviewAsync(ResultRowViewModel row)
    {
        if (row == null) return;

        foreach (var r in Results) r.IsPlaying = false;
        row.IsPlaying = true;

        try
        {
            var downloader = new PreviewDownloader();
            var path = await downloader.DownloadAsync(row.Track);

            Session.SetStatus($"Previewing: {row.Name}");

            // Play the downloaded temp file through the existing preview path.
            // CurrentTrack is cleared inside PlayLocalFile, so the playback bar
            // renders NowPlayingThumbnail/NowPlayingSourceUrl from the preview fields.
            Session.Playback.PlayLocalFile(
                path,
                row.Name,
                thumbnailPath: row.ThumbnailUrl,
                sourceUrl: row.SourceUrl);
        }
        catch (Exception ex)
        {
            Session.SetError($"Preview failed: {ex.Message}");
            row.IsPlaying = false;
        }
    }

    private async Task AddToPlaylistAsync(ResultRowViewModel row)
    {
        if (row?.SelectedPlaylist == null) return;

        var url = row.SourceUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            Session.SetError("Could not build a source URL for this result.");
            return;
        }

        var result = await Session.Repo.AddTracksAsync(row.SelectedPlaylist.Id, url);
        if (!result.Success)
            Session.SetError(result.Error ?? "Failed to add track.");
        else
            Session.SetStatus($"Added '{row.Name}' to '{row.SelectedPlaylist.Name}'.");
    }

    #endregion
}