using Microsoft.Extensions.Logging.Abstractions;
using TubeDrop.Core.Cover;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Settings;

namespace TubeDrop.Tests.Cover;

public sealed class CoverArtEnricherTests
{
    private sealed class FixedSettings(AppSettings s) : ISettingsStore
    {
        public AppSettings Current { get; } = s;
        public event EventHandler<AppSettings>? Changed { add { } remove { } }
        public void Update(Func<AppSettings, AppSettings> mutate) { }
    }

    private sealed class StubLookup(bool available, CoverLookupResult result) : ICoverImageLookup
    {
        public bool IsAvailable => available;
        public Task<CoverLookupResult> LookupAsync(byte[] imageBytes, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private static TrackInfo NoArtistWithCover() => new()
    {
        SourcePath = "x.mp3", Artist = "", Title = "Hurricane", CoverArt = [1, 2, 3],
    };

    [Fact]
    public async Task Enrich_RecognizesArtistFromCover_BeforeSearching()
    {
        var enricher = new CoverArtEnricher(
            new StubLookup(true, new CoverLookupResult("I Prevail Hurricane", [])),
            new FixedSettings(new AppSettings { CoverSearchEnabled = true }),
            NullLogger<CoverArtEnricher>.Instance);

        var result = await enricher.EnrichAsync(NoArtistWithCover());

        Assert.Equal("I Prevail", result.Artist);
        Assert.Equal("Hurricane", result.Title);
    }

    [Fact]
    public async Task Enrich_Disabled_Unchanged()
    {
        var enricher = new CoverArtEnricher(
            new StubLookup(true, new CoverLookupResult("I Prevail Hurricane", [])),
            new FixedSettings(new AppSettings { CoverSearchEnabled = false }),
            NullLogger<CoverArtEnricher>.Instance);

        Assert.Equal("", (await enricher.EnrichAsync(NoArtistWithCover())).Artist);
    }

    [Fact]
    public async Task Enrich_AlreadyHasArtist_Unchanged()
    {
        var enricher = new CoverArtEnricher(
            new StubLookup(true, new CoverLookupResult("Other Artist Song", [])),
            new FixedSettings(new AppSettings { CoverSearchEnabled = true }),
            NullLogger<CoverArtEnricher>.Instance);
        var tagged = new TrackInfo { SourcePath = "x.mp3", Artist = "Queen", Title = "Hurricane", CoverArt = [1] };

        Assert.Equal("Queen", (await enricher.EnrichAsync(tagged)).Artist);
    }

    [Fact]
    public async Task Enrich_NoCover_Unchanged()
    {
        var enricher = new CoverArtEnricher(
            new StubLookup(true, new CoverLookupResult("I Prevail Hurricane", [])),
            new FixedSettings(new AppSettings { CoverSearchEnabled = true }),
            NullLogger<CoverArtEnricher>.Instance);
        var noCover = new TrackInfo { SourcePath = "x.mp3", Artist = "", Title = "Hurricane", CoverArt = null };

        Assert.Equal("", (await enricher.EnrichAsync(noCover)).Artist);
    }
}
