using Microsoft.Extensions.Logging.Abstractions;
using TubeDrop.Core.Fingerprint;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Settings;

namespace TubeDrop.Tests.Fingerprint;

public sealed class AcoustIdResponseParserTests
{
    [Fact]
    public void Parse_PicksHighestScoringRecording()
    {
        const string json = """
            {
              "status": "ok",
              "results": [
                { "score": 0.62, "recordings": [ { "title": "Low One", "artists": [ { "name": "Artist A" } ] } ] },
                { "score": 0.95, "recordings": [ { "title": "Best Title", "artists": [ { "name": "Real Artist" } ] } ] }
              ]
            }
            """;

        var match = AcoustIdResponseParser.Parse(json);

        Assert.NotNull(match);
        Assert.Equal("Real Artist", match.Artist);
        Assert.Equal("Best Title", match.Title);
        Assert.Equal(0.95, match.Score, precision: 3);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"status":"ok","results":[]}""")]
    [InlineData("""{"results":[{"score":0.9,"recordings":[]}]}""")]
    [InlineData("not json")]
    public void Parse_Degenerate_ReturnsNull(string json)
    {
        Assert.Null(AcoustIdResponseParser.Parse(json));
    }
}

public sealed class FpcalcParseTests
{
    [Fact]
    public void Parse_FpcalcJson_ExtractsDurationAndFingerprint()
    {
        var fp = FpcalcFingerprinter.Parse("""{"duration": 245.3, "fingerprint": "AQABz0m..."}""");

        Assert.NotNull(fp);
        Assert.Equal(245, fp.DurationSeconds);
        Assert.Equal("AQABz0m...", fp.Fingerprint);
    }

    [Fact]
    public void Parse_NoFingerprint_ReturnsNull() =>
        Assert.Null(FpcalcFingerprinter.Parse("""{"duration": 100}"""));
}

public sealed class AcoustIdEnricherTests
{
    private sealed class FixedSettings(AppSettings s) : ISettingsStore
    {
        public AppSettings Current { get; } = s;
        public event EventHandler<AppSettings>? Changed { add { } remove { } }
        public void Update(Func<AppSettings, AppSettings> mutate) { }
    }

    private sealed class StubFingerprinter(bool available, AudioFingerprint? result) : IAudioFingerprinter
    {
        public bool IsAvailable => available;
        public Task<AudioFingerprint?> ComputeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class StubAcoustId(AcoustIdMatch? match) : IAcoustIdClient
    {
        public Task<AcoustIdMatch?> LookupAsync(AudioFingerprint fp, string apiKey, CancellationToken ct = default) =>
            Task.FromResult(match);
    }

    private static TrackInfo WeakTrack() => new()
    {
        SourcePath = "x.mp3", Artist = "", Title = "unknown 001", Origin = TrackMetadataOrigin.FilenameHeuristics,
    };

    [Fact]
    public async Task Enrich_Disabled_ReturnsUnchanged()
    {
        var enricher = new AcoustIdEnricher(
            new StubFingerprinter(true, new AudioFingerprint(200, "FP")),
            new StubAcoustId(new AcoustIdMatch("Real", "Song", 0.9)),
            new FixedSettings(new AppSettings { AcoustIdEnabled = false }),
            NullLogger<AcoustIdEnricher>.Instance);

        var result = await enricher.EnrichAsync(WeakTrack());

        Assert.Equal("", result.Artist);
    }

    [Fact]
    public async Task Enrich_NoFpcalc_ReturnsUnchanged()
    {
        var enricher = new AcoustIdEnricher(
            new StubFingerprinter(available: false, null),
            new StubAcoustId(new AcoustIdMatch("Real", "Song", 0.9)),
            new FixedSettings(new AppSettings { AcoustIdEnabled = true, AcoustIdApiKey = "k" }),
            NullLogger<AcoustIdEnricher>.Instance);

        var result = await enricher.EnrichAsync(WeakTrack());

        Assert.Equal("", result.Artist);
    }

    [Fact]
    public async Task Enrich_GoodMatch_ReplacesArtistAndTitle()
    {
        var enricher = new AcoustIdEnricher(
            new StubFingerprinter(true, new AudioFingerprint(200, "FP")),
            new StubAcoustId(new AcoustIdMatch("Daft Punk", "One More Time", 0.92)),
            new FixedSettings(new AppSettings { AcoustIdEnabled = true, AcoustIdApiKey = "k" }),
            NullLogger<AcoustIdEnricher>.Instance);

        var result = await enricher.EnrichAsync(WeakTrack());

        Assert.Equal("Daft Punk", result.Artist);
        Assert.Equal("One More Time", result.Title);
        Assert.Equal(TrackMetadataOrigin.Tags, result.Origin);
    }

    [Fact]
    public async Task Enrich_LowScore_ReturnsUnchanged()
    {
        var enricher = new AcoustIdEnricher(
            new StubFingerprinter(true, new AudioFingerprint(200, "FP")),
            new StubAcoustId(new AcoustIdMatch("Maybe", "Maybe", 0.3)),
            new FixedSettings(new AppSettings { AcoustIdEnabled = true, AcoustIdApiKey = "k" }),
            NullLogger<AcoustIdEnricher>.Instance);

        var result = await enricher.EnrichAsync(WeakTrack());

        Assert.Equal("", result.Artist);
    }

    [Fact]
    public async Task Enrich_WellTaggedTrack_NotFingerprinted()
    {
        var enricher = new AcoustIdEnricher(
            new StubFingerprinter(true, new AudioFingerprint(200, "FP")),
            new StubAcoustId(new AcoustIdMatch("Other", "Other", 0.99)),
            new FixedSettings(new AppSettings { AcoustIdEnabled = true, AcoustIdApiKey = "k" }),
            NullLogger<AcoustIdEnricher>.Instance);
        var tagged = new TrackInfo
        {
            SourcePath = "x.mp3", Artist = "Queen", Title = "Bohemian Rhapsody", Origin = TrackMetadataOrigin.Tags,
        };

        var result = await enricher.EnrichAsync(tagged);

        Assert.Equal("Queen", result.Artist); // untouched — good metadata
    }
}
