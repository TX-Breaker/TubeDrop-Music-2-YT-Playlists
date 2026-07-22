namespace TubeDrop.Core.Matching;

public enum CandidateSource
{
    YtmSong,
    YtmVideo,
    YouTube,
}

/// <summary>One normalized search result, ready for scoring (§7).</summary>
public sealed record MatchCandidate
{
    public required string VideoId { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<string> Artists { get; init; } = [];
    public int DurationSeconds { get; init; }
    public string Channel { get; init; } = "";
    public string Album { get; init; } = "";
    public IReadOnlyList<string> Badges { get; init; } = [];
    public CandidateSource Source { get; init; }

    /// <summary>Verified-artist / official-artist-channel signal from the source.</summary>
    public bool IsOfficialArtistChannel { get; init; }
}
