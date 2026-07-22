using System.Text.Json;
using Microsoft.Extensions.Logging;
using TubeDrop.Core.Playlists;
using TubeDrop.InnerTube.Http;
using TubeDrop.InnerTube.Json;

namespace TubeDrop.InnerTube.Playlists;

/// <summary>
/// Playlist mutations via WEB_REMIX youtubei endpoints (§5).
///
/// TODO(fixtures): these endpoints require an authenticated session, so no real
/// fixtures could be captured yet (§15). Parsing below targets the documented
/// response shapes and is written defensively; the transport's capture mode
/// must be enabled during the first signed-in spike run to dump real fixtures
/// into tests/fixtures, and the parsers then hardened against them.
/// </summary>
public sealed class PlaylistClient(InnerTubeTransport transport, ILogger<PlaylistClient> logger) : IPlaylistClient
{
    /// <summary>Hard cap on continuation pages (~20 items/page → ~2000). Truncation is logged, never silent (§11).</summary>
    private const int MaxContinuationPages = 100;

    public async Task<string> CreatePlaylistAsync(
        string title, string description, PlaylistPrivacy privacy,
        IReadOnlyList<string>? initialVideoIds = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = description,
            ["privacyStatus"] = privacy switch
            {
                PlaylistPrivacy.Public => "PUBLIC",
                PlaylistPrivacy.Unlisted => "UNLISTED",
                _ => "PRIVATE",
            },
        };
        if (initialVideoIds is { Count: > 0 })
        {
            body["videoIds"] = initialVideoIds;
        }

        var root = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "playlist/create", body, ct)
            .ConfigureAwait(false);

        var playlistId = root.GetString("playlistId");
        if (string.IsNullOrEmpty(playlistId))
        {
            throw new InnerTubeException("playlist/create returned no playlistId — schema may have changed");
        }

        return playlistId;
    }

    public async Task<IReadOnlyList<AddedItem>> AddItemsAsync(
        string playlistId, IReadOnlyList<string> videoIds, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["playlistId"] = StripVl(playlistId),
            ["actions"] = videoIds
                .Select(v => new Dictionary<string, string>
                {
                    ["action"] = "ACTION_ADD_VIDEO",
                    ["addedVideoId"] = v,
                })
                .ToList(),
        };

        var root = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "browse/edit_playlist", body, ct)
            .ConfigureAwait(false);

        var status = root.GetString("status");
        if (status is not null && !status.Contains("SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InnerTubeException($"edit_playlist add returned status {status}");
        }

        var results = new List<AddedItem>();
        foreach (var edit in root.GetArray("playlistEditResults"))
        {
            var data = edit.Get("playlistEditVideoAddedResultData");
            if (data is { } d)
            {
                var videoId = d.GetString("videoId") ?? "";
                var setVideoId = d.GetString("setVideoId");
                if (videoId.Length > 0)
                {
                    results.Add(new AddedItem(videoId, setVideoId));
                }
            }
        }

        // Schema drift guard: adds reported success but no per-item results →
        // callers treat missing setVideoIds as non-undoable and fail loudly.
        return results;
    }

    public async Task RemoveItemsAsync(
        string playlistId, IReadOnlyList<PlaylistItem> items, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["playlistId"] = StripVl(playlistId),
            ["actions"] = items
                .Select(i => new Dictionary<string, string>
                {
                    ["action"] = "ACTION_REMOVE_VIDEO_BY_SET_VIDEO_ID",
                    ["setVideoId"] = i.SetVideoId,
                    ["removedVideoId"] = i.VideoId,
                })
                .ToList(),
        };

        var root = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "browse/edit_playlist", body, ct)
            .ConfigureAwait(false);

        var status = root.GetString("status");
        if (status is not null && !status.Contains("SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InnerTubeException($"edit_playlist remove returned status {status}");
        }
    }

    public async Task DeletePlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        await transport.PostAsync(InnerTubeTransport.MusicOrigin, "playlist/delete",
            new Dictionary<string, object?> { ["playlistId"] = StripVl(playlistId) }, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlaylistSummary>> GetLibraryPlaylistsAsync(CancellationToken ct = default)
    {
        var root = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "browse",
            new Dictionary<string, object?> { ["browseId"] = "FEmusic_liked_playlists" }, ct)
            .ConfigureAwait(false);

        // Library grid: singleColumnBrowseResultsRenderer → tabs → sectionList →
        // gridRenderer.items[].musicTwoRowItemRenderer. Defensive throughout.
        var results = new List<PlaylistSummary>();
        CollectLibraryPlaylists(root, results);

        // Follow continuations so a large library isn't truncated (§11).
        var token = ContinuationPaging.FindToken(root);
        var pages = 0;
        while (token is not null && pages++ < MaxContinuationPages)
        {
            ct.ThrowIfCancellationRequested();
            var page = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "browse",
                new Dictionary<string, object?> { ["continuation"] = token }, ct).ConfigureAwait(false);

            var before = results.Count;
            foreach (var array in ContinuationPaging.FindItemArrays(page))
            {
                foreach (var item in array.EnumerateArray())
                {
                    TryAddLibraryPlaylist(item, results);
                }
            }

            token = ContinuationPaging.FindToken(page);
            if (results.Count == before)
            {
                break; // no progress — stop rather than loop forever
            }
        }

        if (token is not null)
        {
            logger.LogWarning("Library listing hit the {Cap}-page cap — {Count} playlists may be incomplete",
                MaxContinuationPages, results.Count);
        }

        return results;
    }

    public async Task<IReadOnlyList<PlaylistItem>> GetPlaylistItemsAsync(
        string playlistId, CancellationToken ct = default)
    {
        var browseId = playlistId.StartsWith("VL", StringComparison.Ordinal) ? playlistId : "VL" + playlistId;
        var root = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "browse",
            new Dictionary<string, object?> { ["browseId"] = browseId }, ct)
            .ConfigureAwait(false);

        var items = new List<PlaylistItem>();
        CollectPlaylistItems(root, items);

        // Follow continuations so a large playlist snapshot is complete — undo of a
        // delete rebuilds from this, so truncation would silently lose tracks (§10/§11).
        var token = ContinuationPaging.FindToken(root);
        var pages = 0;
        while (token is not null && pages++ < MaxContinuationPages)
        {
            ct.ThrowIfCancellationRequested();
            var page = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "browse",
                new Dictionary<string, object?> { ["continuation"] = token }, ct).ConfigureAwait(false);

            var before = items.Count;
            foreach (var array in ContinuationPaging.FindItemArrays(page))
            {
                foreach (var item in array.EnumerateArray())
                {
                    TryAddPlaylistItem(item, items);
                }
            }

            token = ContinuationPaging.FindToken(page);
            if (items.Count == before)
            {
                break;
            }
        }

        if (token is not null)
        {
            logger.LogWarning("Playlist {Playlist} hit the {Cap}-page cap — snapshot of {Count} items may be incomplete",
                playlistId, MaxContinuationPages, items.Count);
        }

        return items;
    }

    /// <summary>Recursively finds musicPlaylistShelfRenderer contents — resilient to wrapper renames.</summary>
    private static void CollectPlaylistItems(JsonElement node, List<PlaylistItem> items)
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
                    CollectPlaylistItems(property.Value, items);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    CollectPlaylistItems(item, items);
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

    private static void CollectLibraryPlaylists(JsonElement root, List<PlaylistSummary> results)
    {
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

    private static string StripVl(string playlistId) =>
        playlistId.StartsWith("VL", StringComparison.Ordinal) ? playlistId[2..] : playlistId;
}
