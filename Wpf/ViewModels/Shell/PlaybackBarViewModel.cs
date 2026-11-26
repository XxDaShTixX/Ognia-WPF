using Ognia.Wpf.Session;

namespace Ognia.Wpf.ViewModels.Shell;

public sealed class PlaybackBarViewModel
{
    public PlaybackBarViewModel(AppSession session, PlaybackViewModel playback, DownloadViewModel download)
    {
        Session = session;
        PlaybackViewModel = playback;
        DownloadViewModel = download;
    }

    public AppSession Session { get; }
    public PlaybackViewModel PlaybackViewModel { get; }
    public DownloadViewModel DownloadViewModel { get; }
}