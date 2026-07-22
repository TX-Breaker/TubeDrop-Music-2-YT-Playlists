using System.Text.Json;
using TubeDrop.Core.Matching;
using TubeDrop.InnerTube.Search;

namespace TubeDrop.Tests.InnerTube;

/// <summary>
/// Parser tests against REAL captured responses (tests/fixtures, captured
/// 2026-07-22 via unauthenticated InnerTube search — spec §15 fixture-first).
/// </summary>
public sealed class SearchResponseParserTests
{
    private static JsonElement LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ParseYtmSearch_Songs_ExtractsFullCandidates()
    {
        var root = LoadFixture("ytm_search_songs.json");

        var results = SearchResponseParser.ParseYtmSearch(root, CandidateSource.YtmSong);

        Assert.NotEmpty(results);
        var first = results[0];
        Assert.Equal("JhulBGMA7G4", first.VideoId);
        Assert.Equal("Harder, Better, Faster, Stronger", first.Title);
        Assert.Contains("Daft Punk", first.Artists);
        Assert.Equal("Discovery", first.Album);
        Assert.Equal(3 * 60 + 47, first.DurationSeconds);
        Assert.Equal(CandidateSource.YtmSong, first.Source);
        Assert.True(first.IsOfficialArtistChannel); // MUSIC_VIDEO_TYPE_ATV
    }

    [Fact]
    public void ParseYtmSearch_Videos_ExtractsCandidates()
    {
        var root = LoadFixture("ytm_search_videos.json");

        var results = SearchResponseParser.ParseYtmSearch(root, CandidateSource.YtmVideo);

        Assert.NotEmpty(results);
        var first = results[0];
        Assert.Equal("gAjR4_CbPpQ", first.VideoId);
        Assert.Equal("Harder, Better, Faster, Stronger", first.Title);
        Assert.Contains("Daft Punk", first.Artists);
        Assert.Equal(3 * 60 + 43, first.DurationSeconds);
    }

    [Fact]
    public void ParseYtmSearch_NonLatinQuery_ExtractsCandidates()
    {
        var root = LoadFixture("ytm_search_nolatin.json");

        var results = SearchResponseParser.ParseYtmSearch(root, CandidateSource.YtmSong);

        Assert.NotEmpty(results);
        Assert.Equal("3NNhrqHZqlI", results[0].VideoId);
        Assert.Equal("Lemon", results[0].Title);
        Assert.Contains("Kenshi Yonezu", results[0].Artists);
    }

    [Fact]
    public void ParseYouTubeSearch_ExtractsCandidates()
    {
        var root = LoadFixture("yt_search.json");

        var results = SearchResponseParser.ParseYouTubeSearch(root);

        Assert.NotEmpty(results);
        var first = results[0];
        Assert.Equal("gAjR4_CbPpQ", first.VideoId);
        Assert.StartsWith("Daft Punk - Harder, Better, Faster, Stronger", first.Title);
        Assert.Equal("Daft Punk", first.Channel);
        Assert.Equal(3 * 60 + 43, first.DurationSeconds);
        Assert.True(first.IsOfficialArtistChannel); // VERIFIED_ARTIST badge
        Assert.Equal(CandidateSource.YouTube, first.Source);
    }

    [Fact]
    public void ParseYouTubeSearch_UnverifiedChannel_NotOfficial()
    {
        var root = LoadFixture("yt_search.json");

        var results = SearchResponseParser.ParseYouTubeSearch(root);

        // The fixture contains at least one re-upload from a non-verified channel.
        Assert.Contains(results, r => !r.IsOfficialArtistChannel);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"contents":{}}""")]
    [InlineData("""{"contents":{"tabbedSearchResultsRenderer":{"tabs":[]}}}""")]
    [InlineData("""{"contents":{"tabbedSearchResultsRenderer":{"tabs":[{"tabRenderer":{}}]}}}""")]
    public void ParseYtmSearch_DegenerateShapes_ReturnEmpty_NeverThrow(string json)
    {
        using var doc = JsonDocument.Parse(json);

        var results = SearchResponseParser.ParseYtmSearch(doc.RootElement, CandidateSource.YtmSong);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"contents":{"twoColumnSearchResultsRenderer":{}}}""")]
    public void ParseYouTubeSearch_DegenerateShapes_ReturnEmpty_NeverThrow(string json)
    {
        using var doc = JsonDocument.Parse(json);

        var results = SearchResponseParser.ParseYouTubeSearch(doc.RootElement);

        Assert.Empty(results);
    }
}
