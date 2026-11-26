using Ognia.Wpf.Navigation;
using Ognia.Wpf.Session;

namespace Ognia.Wpf.ViewModels.Shell;

/// <summary>
/// Composes the left pane's data context. Exists so ShellView has one
/// DataContext root, and future pane VMs (queue, top bar) slot in here.
/// </summary>
public sealed class ShellViewModel
{
    public ShellViewModel(AppSession session, IShellNavigator nav)
    {
        Session = session;
        Left = new LeftPaneViewModel(session, nav);
    }

    public AppSession Session { get; }
    public LeftPaneViewModel Left { get; }
}