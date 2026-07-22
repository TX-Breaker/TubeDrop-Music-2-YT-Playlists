using System.Text.Json;
using TubeDrop.Core.Playlists;
using TubeDrop.InnerTube.Json;

namespace TubeDrop.InnerTube.Playlists;

/// <summary>
/// Parsers for the playlist youtubei endpoints. Shapes verified against real
/// authenticated traffic captured 2026-07-22 (§5, §15):
///
/// - playlist/create → { playlistId } at the top level.
/// - browse/edit_playlist (add) → { status: "STATUS_SUCCEEDED",
///     playlistEditResults[].playlistEditVideoAddedResultData.{videoId, setVideoId} }.
/// - browse (library FEmusic_liked_playlists) → singleColumnBrowseResultsRenderer
///     .tabs[].tabRenderer.content.sectionListRenderer.contents[].gridRenderer
///     .items[].musicTwoRowItemRenderer with navigationEndpoint.browseEndpoint
///     .browseId ("VL"-prefixed) + title runs; the first grid item is the
///     "New playlist" button (createPlaylistEndpoint) and is skipped.
///     Continuations arrive as continuationContents.gridContinuation.items[].
///
/// Still unverified (not exercised in the capture): browse of a playlist's items
/// (musicPlaylistShelfRenderer.contents[].musicResponsiveListItemRenderer
/// .playlistItemData.{videoId, playlistSetVideoId}) — only hit on delete-snapshot.
/// </summary>
public static class PlaylistResponseParser
{
    public static string? ParseCreatedPlaylistId(JsonElement root) => root.GetString("playlistId");

    public static string? ParseStatus(JsonElement root) => root.GetString("status");

    public static IReadOnlyList<AddedItem> ParseAddedItems(JsonElement root)
    {
        var results = new List<AddedItem>();
        foreach (var edit in root.GetArray("playlistEditResults"))
        {
            if (edit.Get("playlistEditVideoAddedResultData") is not { } data)
            {
                continue;
            }

            var videoId = data.GetString("videoId") ?? "";
            var setVideoId = data.GetString("setVideoId");
            if (videoId.Length > 0)
            {
                results.Add(new AddedItem(videoId, setVideoId));
            }
        }

        return results;
    }

    /// <summary>Collects playlists from either the initial grid page or a continuation page.</summary>
    public static void CollectLibraryPage(JsonElement root, List<PlaylistSummary> results)
    {
        // Initial page: singleColumn → grid.
        foreach (var tab in root.GetArray("contents", "singleColumnBrowseResultsRenderer", "tabs"))
        {
            foreach (var section in tab.GetArray("tabRenderer", "content", "sectionListRenderer", "contents"))
            {
                foreach (var item in section.GetArray("gridRenderer", "items"))
                {
                    TryAddLibraryPlaylist(item, results);
                }
            }
        }

        // Continuation page: gridContinuation.items etc.
        foreach (var array in ContinuationPaging.FindItemArrays(root))
        {
            foreach (var item in array.EnumerateArray())
            {
                TryAddLibraryPlaylist(item, results);
            }
        }
    }

    /// <summary>Collects items from either the initial shelf page or a continuation page.</summary>
    public static void CollectPlaylistItemsPage(JsonElement root, List<PlaylistItem> items)
    {
        CollectShelfItems(root, items);
        foreach (var array in ContinuationPaging.FindItemArrays(root))
        {
            foreach (var item in array.EnumerateArray())
            {
                TryAddPlaylistItem(item, items);
            }
        }
    }

    private static void CollectShelfItems(JsonElement node, List<PlaylistItem> items)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (node.Get("musicPlaylistShelfRenderer", "contents") is { } contents)
                {
                    foreach (var item in contents.EnumerateArray())
                    {
                        TryAddPlaylistItem(item, items);
                    }

                    return;
                }

                foreach (var property in node.EnumerateObject())
                {
                    CollectShelfItems(property.Value, items);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    CollectShelfItems(item, items);
                }

                break;
        }
    }

    private static void TryAddPlaylistItem(JsonElement item, List<PlaylistItem> items)
    {
        var data = item.Get("musicResponsiveListItemRenderer", "playlistItemData");
        var videoId = data?.GetString("videoId");
        var setVideoId = data?.GetString("playlistSetVideoId");
        if (!string.IsNullOrEmpty(videoId) && !string.IsNullOrEmpty(setVideoId))
        {
            items.Add(new PlaylistItem(videoId, setVideoId));
        }
    }

    private static void TryAddLibraryPlaylist(JsonElement item, List<PlaylistSummary> results)
    {
        if (item.Get("musicTwoRowItemRenderer") is not { } renderer)
        {
            return;
        }

        var browseId = renderer.GetString("navigationEndpoint", "browseEndpoint", "browseId") ?? "";
        var title = renderer.JoinRuns("title");
        if (browseId.StartsWith("VL", StringComparison.Ordinal) && title.Length > 0 &&
            !results.Any(p => p.PlaylistId == browseId[2..]))
        {
            results.Add(new PlaylistSummary(browseId[2..], title, 0));
        }
    }
}
