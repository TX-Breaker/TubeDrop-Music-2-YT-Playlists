using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Matching;

namespace TubeDrop.Tests.Matching;

public sealed class MatchingEngineTests
{
    private static TrackInfo Track(string artist = "Daft Punk", string title = "One More Time") => new()
    {
        SourcePath = "x.mp3",
        Artist = artist,
        Title = title,
        DurationSeconds = 320,
    };

    private static MatchCandidate Good() => new()
    {
        VideoId = "good",
        Title = "One More Time",
        Artists = ["Daft Punk"],
        Channel = "Daft Punk",
        DurationSeconds = 320,
        IsOfficialArtistChannel = true,
    };

    private static MatchCandidate Bad() => new()
    {
        VideoId = "bad",
        Title = "Something Unrelated Entirely",
        Artists = ["Nobody"],
        Channel = "Nobody",
        DurationSeconds = 95,
    };

    private sealed class FakeSearcher(Func<string, IReadOnlyList<MatchCandidate>> respond) : ICandidateSearcher
    {
        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<MatchCandidate>> SearchAsync(
            string query, SearchScope scope, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult(respond(query));
        }
    }

    private sealed class FakeRefiner(string name, params string[] queries) : IQueryRefiner
    {
        public string Name => name;
        public bool Called { get; private set; }

        public Task<IReadOnlyList<string>> RefineAsync(TrackInfo track, CancellationToken ct = default)
        {
            Called = true;
            return Task.FromResult<IReadOnlyList<string>>(queries);
        }
    }

    [Fact]
    public async Task GoodResult_AutoMatched_NoRefinersCalled()
    {
        var searcher = new FakeSearcher(_ => [Good(), Bad()]);
        var refiner = new FakeRefiner("deterministic", "extra query");
        var engine = new MatchingEngine(searcher, [refiner]);

        var result = await engine.MatchAsync(Track(), new MatchingOptions());

        Assert.Equal(MatchStatus.AutoMatched, result.Status);
        Assert.Equal("good", result.Best!.Candidate.VideoId);
        Assert.False(refiner.Called);
        // Stops after the first query that clears the threshold.
        Assert.Single(searcher.Queries);
    }

    [Fact]
    public async Task NoGoodResult_LadderRuns_FallbackMatched()
    {
        var searcher = new FakeSearcher(q => q == "refined query" ? [Good()] : [Bad()]);
        var refiner = new FakeRefiner("deterministic", "refined query");
        var engine = new MatchingEngine(searcher, [refiner]);

        var result = await engine.MatchAsync(Track(), new MatchingOptions());

        Assert.Equal(MatchStatus.FallbackMatched, result.Status);
        Assert.Equal("deterministic", result.UsedRefiner);
        Assert.Equal("refined query", result.UsedQuery);
    }

    [Fact]
    public async Task NothingGood_Unmatched_KeepsBestForReport()
    {
        var searcher = new FakeSearcher(_ => [Bad()]);
        var engine = new MatchingEngine(searcher, []);

        var result = await engine.MatchAsync(Track(), new MatchingOptions());

        Assert.Equal(MatchStatus.Unmatched, result.Status);
        Assert.NotNull(result.Best); // best-so-far is reported, never silently added
        Assert.Equal("bad", result.Best!.Candidate.VideoId);
    }

    [Fact]
    public async Task AggressiveMode_FlagsBestEffortMatch()
    {
        var searcher = new FakeSearcher(_ => [Bad()]);
        var engine = new MatchingEngine(searcher, []);

        var result = await engine.MatchAsync(Track(), new MatchingOptions { AggressiveMode = true });

        Assert.Equal(MatchStatus.AggressiveMatched, result.Status);
    }

    [Fact]
    public async Task EmptySearchResults_Unmatched()
    {
        var searcher = new FakeSearcher(_ => []);
        var engine = new MatchingEngine(searcher, []);

        var result = await engine.MatchAsync(Track(), new MatchingOptions());

        Assert.Equal(MatchStatus.Unmatched, result.Status);
        Assert.Null(result.Best);
    }

    [Fact]
    public async Task LaddersRunInOrder_StopOnSuccess()
    {
        var searcher = new FakeSearcher(q => q == "first refinement" ? [Good()] : [Bad()]);
        var first = new FakeRefiner("first", "first refinement");
        var second = new FakeRefiner("second", "second refinement");
        var engine = new MatchingEngine(searcher, [first, second]);

        var result = await engine.MatchAsync(Track(), new MatchingOptions());

        Assert.Equal("first", result.UsedRefiner);
        Assert.True(first.Called);
        Assert.False(second.Called);
    }
}
