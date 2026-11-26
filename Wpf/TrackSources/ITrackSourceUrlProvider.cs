namespace Ognia.Wpf.TrackSources;

/// <summary>
/// Builds a canonical public URL for a track given its source-specific ID.
/// One implementation per source; the resolver picks by SourceName.
/// </summary>
public interface ITrackSourceUrlProvider
{
    /// <summary>
    /// Wire name used in TrackModel.SourceName / TrackResponse.SourceName.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Returns the canonical URL, or null if the ID is invalid for this source.
    /// </summary>
    string? BuildUrl(string sourceId);
}