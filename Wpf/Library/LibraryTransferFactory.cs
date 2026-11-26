using Ognia.Wpf.Repos;
using Ognia.Wpf.Services;

namespace Ognia.Wpf.Library;

public static class LibraryTransferFactory
{
    public static ILibraryTransfer Create(IMusicRepo repo, bool isGuest)
        => isGuest
            ? new LocalLibraryTransfer()
            : new ApiLibraryTransfer(repo);
}