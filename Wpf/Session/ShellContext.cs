using Ognia.Wpf.ViewModels.RightPane;
using Ognia.Wpf.ViewModels.Shell;

namespace Ognia.Wpf.Session;

public sealed class ShellContext : IDisposable
{
    public AppSession Session { get; }
    public PlaybackViewModel Playback { get; }
    public DownloadViewModel Download { get; }
    public TrackDetailsViewModel Details { get; }
    public MultiTrackSelectionViewModel MultiSelection { get; }

    public ShellContext(AppSession session)
    {
        // Reference
        Session = session;

        // Instantiate
        Playback = new PlaybackViewModel();
        Download = new DownloadViewModel();
        Details = new TrackDetailsViewModel(session);
        MultiSelection = new MultiTrackSelectionViewModel(session);

        // Initialize
        session.AttachPlayer(Playback, Download);
        session.Details = Details;
    }

    public void Dispose()
    {
        Session.Details = null;

        Details.Dispose();
        Playback.Dispose();
    }
}