using Microsoft.Extensions.Logging.Abstractions;
using TubeDrop.Core.Cover;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Matching.Refiners;
using TubeDrop.Core.Settings;

namespace TubeDrop.Tests.Matching;

public sealed class CoverImageRefinerTests
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

    private static TrackInfo TrackWithCover(string genre = "") => new()
    {
        SourcePath = "x.mp3", Title = "unknown", Artist = "",
        Genre = genre, CoverArt = [1, 2, 3, 4],
    };

    [Fact]
    public async Task Disabled_ReturnsNoQueries()
    {
        var refiner = new CoverImageRefiner(
            new StubLookup(true, new CoverLookupResult("Daft Punk One More Time", [])),
            new FixedSettings(new AppSettings { CoverSearchEnabled = false }),
            NullLogger<CoverImageRefiner>.Instance);

        Assert.Empty(await refiner.RefineAsync(TrackWithCover()));
    }

    [Fact]
    public async Task NoCoverArt_ReturnsNoQueries()
    {
        var refiner = new CoverImageRefiner(
            new StubLookup(true, new CoverLookupResult("Something", [])),
            new FixedSettings(new AppSettings { CoverSearchEnabled = true }),
            NullLogger<CoverImageRefiner>.Instance);
        var noCover = new TrackInfo { SourcePath = "x.mp3", Title = "t", CoverArt = null };

        Assert.Empty(await refiner.RefineAsync(noCover));
    }

    [Fact]
    public async Task EnabledWithBestGuess_ProducesQueries()
    {
        var refiner = new CoverImageRefiner(
            new StubLookup(true, new CoverLookupResult("Daft Punk One More Time", ["Discovery album"])),
            new FixedSettings(new AppSettings { CoverSearchEnabled = true }),
            NullLogger<CoverImageRefiner>.Instance);

        var queries = await refiner.RefineAsync(TrackWithCover());

        Assert.Contains("Daft Punk One More Time", queries);
        Assert.Contains("Discovery album", queries);
    }

    [Fact]
    public async Task GenreAppendedAsHint()
    {
        var refiner = new CoverImageRefiner(
            new StubLookup(true, new CoverLookupResult("Some Artist Song", [])),
            new FixedSettings(new AppSettings { CoverSearchEnabled = true }),
            NullLogger<CoverImageRefiner>.Instance);

        var queries = await refiner.RefineAsync(TrackWithCover(genre: "Hardcore"));

        Assert.Contains("Some Artist Song Hardcore", queries);
    }

    [Fact]
    public async Task EmptyResult_ReturnsNoQueries()
    {
        var refiner = new CoverImageRefiner(
            new StubLookup(true, CoverLookupResult.Empty),
            new FixedSettings(new AppSettings { CoverSearchEnabled = true }),
            NullLogger<CoverImageRefiner>.Instance);

        Assert.Empty(await refiner.RefineAsync(TrackWithCover()));
    }
}
