using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Matching.Refiners;
using TubeDrop.Core.Settings;

namespace TubeDrop.Tests.Matching;

public sealed class DeterministicRefinerTests
{
    private static TrackInfo Track(string artist, string title) => new()
    {
        SourcePath = "x.mp3",
        Artist = artist,
        Title = title,
    };

    [Fact]
    public async Task Refine_StripsBracketsAndFeaturing()
    {
        var refiner = new DeterministicRefiner();

        var queries = await refiner.RefineAsync(Track("Drake feat. Rihanna", "Take Care (Album Version)"));

        Assert.Contains(queries, q => q.Contains("Take Care") && !q.Contains("Album Version"));
        Assert.All(queries, q => Assert.DoesNotContain("feat", q, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refine_SwapsArtistAndTitle()
    {
        var refiner = new DeterministicRefiner();

        var queries = await refiner.RefineAsync(Track("Queen", "Bohemian Rhapsody"));

        Assert.Contains("Queen Bohemian Rhapsody", queries);
        Assert.Contains("Bohemian Rhapsody Queen", queries);
    }

    [Fact]
    public async Task Refine_NonLatin_AddsAsciiTransliteration()
    {
        var refiner = new DeterministicRefiner();

        var queries = await refiner.RefineAsync(Track("米津玄師", "Lemon"));

        // AnyAscii romanizes Han to a Latin form (Mandarin-based); the exact
        // spelling is not guaranteed, but a pure-ASCII variant must appear so
        // YouTube search has something Latin to match on.
        Assert.Contains(queries, q => q.Any(char.IsLetter) && q.All(c => c < 128) && !q.Contains("米"));
    }

    [Theory]
    [InlineData("01 - Song", "Song")]
    [InlineData("12. Another", "Another")]
    public void Clean_RemovesLeadingTrackNumbers(string input, string expected)
    {
        Assert.Equal(expected, DeterministicRefiner.Clean(input));
    }
}

public sealed class OnnxQueryRefinerTests
{
    private sealed class NoModel : IModelProvider
    {
        public bool IsModelAvailable => false;
        public string ModelDirectory => "";
    }

    [Fact]
    public async Task Refine_NoModel_ReturnsEmpty_NeverFakes()
    {
        var refiner = new OnnxQueryRefiner(new NoModel(), NullLogger<OnnxQueryRefiner>.Instance);

        var queries = await refiner.RefineAsync(new TrackInfo { SourcePath = "x", Title = "t" });

        Assert.Empty(queries);
    }

    [Fact]
    public void LocalModelProvider_EmptyDir_NotAvailable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TubeDropTests", Guid.NewGuid().ToString("N"));
        var provider = new LocalModelProvider(dir);

        Assert.False(provider.IsModelAvailable);
    }
}

public sealed class CloudQueryRefinerTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            });
    }

    private sealed class FixedSettings(AppSettings settings) : ISettingsStore
    {
        public AppSettings Current { get; } = settings;
        public event EventHandler<AppSettings>? Changed { add { } remove { } }
        public void Update(Func<AppSettings, AppSettings> mutate) { }
    }

    [Fact]
    public void ParseQueries_ExtractsLinesFromMessagesResponse()
    {
        const string json = """
            {"content":[{"type":"text","text":"Daft Punk One More Time\nOne More Time Daft Punk"}]}
            """;

        var queries = CloudQueryRefiner.ParseQueries(json);

        Assert.Equal(2, queries.Count);
        Assert.Contains("Daft Punk One More Time", queries);
    }

    [Fact]
    public async Task Refine_Disabled_ReturnsEmpty_NoHttpCall()
    {
        var settings = new FixedSettings(new AppSettings { CloudRefinerEnabled = false });
        using var http = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "boom"));
        var refiner = new CloudQueryRefiner(http, settings, NullLogger<CloudQueryRefiner>.Instance);

        var queries = await refiner.RefineAsync(new TrackInfo { SourcePath = "x", Title = "t" });

        Assert.Empty(queries);
    }

    [Fact]
    public async Task Refine_Enabled_ParsesResponse()
    {
        var settings = new FixedSettings(new AppSettings
        {
            CloudRefinerEnabled = true,
            CloudRefinerApiKey = "sk-test",
        });
        const string body = """{"content":[{"type":"text","text":"clean query one\nclean query two"}]}""";
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, body));
        var refiner = new CloudQueryRefiner(http, settings, NullLogger<CloudQueryRefiner>.Instance);

        var queries = await refiner.RefineAsync(new TrackInfo
        {
            SourcePath = "x", Artist = "A", Title = "Messy Title",
        });

        Assert.Contains("clean query one", queries);
    }

    [Fact]
    public async Task Refine_HttpError_ReturnsEmpty_NeverThrows()
    {
        var settings = new FixedSettings(new AppSettings
        {
            CloudRefinerEnabled = true,
            CloudRefinerApiKey = "sk-test",
        });
        using var http = new HttpClient(new StubHandler(HttpStatusCode.TooManyRequests, "rate limited"));
        var refiner = new CloudQueryRefiner(http, settings, NullLogger<CloudQueryRefiner>.Instance);

        var queries = await refiner.RefineAsync(new TrackInfo { SourcePath = "x", Title = "t" });

        Assert.Empty(queries);
    }
}
