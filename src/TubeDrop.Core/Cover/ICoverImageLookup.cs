namespace TubeDrop.Core.Cover;

/// <summary>Best-guess text a reverse-image search returned for a cover.</summary>
/// <param name="BestGuess">The single best-guess label (e.g. "artist – album"), or empty.</param>
/// <param name="Suggestions">Related text/entities that may name the artist or title.</param>
public sealed record CoverLookupResult(string BestGuess, IReadOnlyList<string> Suggestions)
{
    public static readonly CoverLookupResult Empty = new("", []);

    public bool IsEmpty => BestGuess.Length == 0 && Suggestions.Count == 0;
}

/// <summary>
/// Reverse-image search for embedded cover art. Implemented (in the app layer)
/// on top of the user's existing signed-in Google session in WebView2 — no API
/// key, no cloud console (by design). Best-effort: returns Empty on anything
/// that fails or is unavailable.
/// </summary>
public interface ICoverImageLookup
{
    bool IsAvailable { get; }

    Task<CoverLookupResult> LookupAsync(byte[] imageBytes, CancellationToken ct = default);
}

/// <summary>Lookup disabled / unavailable — always Empty.</summary>
public sealed class NullCoverImageLookup : ICoverImageLookup
{
    public bool IsAvailable => false;

    public Task<CoverLookupResult> LookupAsync(byte[] imageBytes, CancellationToken ct = default) =>
        Task.FromResult(CoverLookupResult.Empty);
}
