using Ognia.Wpf.Repos;

namespace Ognia.Wpf.Services;

public static class LibraryTransferFactory
{
    public static ILibraryTransfer Create(IMusicRepo repo, bool isGuest)
        => isGuest
            ? new LocalLibraryTransfer()
            : new ApiLibraryTransfer(repo);
}