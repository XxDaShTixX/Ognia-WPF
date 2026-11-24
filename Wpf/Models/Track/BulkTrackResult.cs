namespace Ognia.Wpf.Models.Track;

public sealed record BulkTrackResult(
    int Processed,
    int Skipped,
    IReadOnlyList<string> Errors);