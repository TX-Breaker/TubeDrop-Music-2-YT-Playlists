using System.Text.Json;
using TubeDrop.Core.Playlists;
using TubeDrop.InnerTube.Playlists;

namespace TubeDrop.Tests.InnerTube;

/// <summary>
/// Parser tests against fixtures whose shapes were confirmed from real
/// authenticated traffic (create / edit_playlist add / library browse verified
/// 2026-07-22). Fixture ids/titles are synthetic — the real capture stays local.
/// </summary>
public sealed class PlaylistResponseParserTests
{
    private static JsonElement Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ParseCreatedPlaylistId_TopLevel()
    {
        Assert.Equal("PLtest12345", PlaylistResponseParser.ParseCreatedPlaylistId(Load("playlist_create.json")));
    }

    [Fact]
    public void ParseAddedItems_ExtractsVideoAndSetVideoIds()
    {
        var added = PlaylistResponseParser.ParseAddedItems(Load("edit_playlist_add.json"));

        Assert.Equal(2, added.Count);
        Assert.Equal(new AddedItem("vidAAA", "SETAAA"), added[0]);
        Assert.Equal(new AddedItem("vidBBB", "SETBBB"), added[1]);
    }

    [Fact]
    public void ParseStatus_ReadsSucceeded()
    {
        Assert.Equal("STATUS_SUCCEEDED", PlaylistResponseParser.ParseStatus(Load("edit_playlist_add.json")));
    }

    [Fact]
    public void CollectLibraryPage_Initial_SkipsCreateButton_ExtractsVlPlaylists()
    {
        var results = new List<PlaylistSummary>();

        PlaylistResponseParser.CollectLibraryPage(Load("browse_library_initial.json"), results);

        // "New playlist" button (createPlaylistEndpoint, no VL browseId) is skipped.
        Assert.Equal(2, results.Count);
        Assert.Equal("PL_aaa", results[0].PlaylistId);
        Assert.Equal("My Mix", results[0].Title);
        Assert.Equal("PL_bbb", results[1].PlaylistId);
    }

    [Fact]
    public void CollectLibraryPage_Continuation_ExtractsFromGridContinuation()
    {
        var results = new List<PlaylistSummary>();

        PlaylistResponseParser.CollectLibraryPage(Load("browse_library_continuation.json"), results);

        Assert.Single(results);
        Assert.Equal("PL_ccc", results[0].PlaylistId);
        Assert.Equal("Chill", results[0].Title);
    }

    [Fact]
    public void CollectLibraryPage_InitialThenContinuation_Dedupes()
    {
        var results = new List<PlaylistSummary>();

        PlaylistResponseParser.CollectLibraryPage(Load("browse_library_initial.json"), results);
        PlaylistResponseParser.CollectLibraryPage(Load("browse_library_initial.json"), results);

        // Same page twice must not double the playlists.
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void CollectPlaylistItemsPage_ExtractsVideoAndSetVideoIds()
    {
        var items = new List<PlaylistItem>();

        PlaylistResponseParser.CollectPlaylistItemsPage(Load("browse_playlist_items.json"), items);

        Assert.Equal(2, items.Count);
        Assert.Equal(new PlaylistItem("pvid1", "PSET1"), items[0]);
        Assert.Equal(new PlaylistItem("pvid2", "PSET2"), items[1]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"contents":{}}""")]
    [InlineData("""{"playlistEditResults":[]}""")]
    public void Parsers_DegenerateShapes_NeverThrow(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Null(PlaylistResponseParser.ParseCreatedPlaylistId(root));
        Assert.Empty(PlaylistResponseParser.ParseAddedItems(root));
        var lib = new List<PlaylistSummary>();
        PlaylistResponseParser.CollectLibraryPage(root, lib);
        Assert.Empty(lib);
        var items = new List<PlaylistItem>();
        PlaylistResponseParser.CollectPlaylistItemsPage(root, items);
        Assert.Empty(items);
    }
}
