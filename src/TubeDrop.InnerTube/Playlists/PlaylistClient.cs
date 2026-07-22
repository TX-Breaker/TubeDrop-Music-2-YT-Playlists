using System.Text.Json;
using Microsoft.Extensions.Logging;
using TubeDrop.Core.Playlists;
using TubeDrop.InnerTube.Http;
using TubeDrop.InnerTube.Json;

namespace TubeDrop.InnerTube.Playlists;

/// <summary>
/// Playlist mutations + library/item reads via WEB_REMIX youtubei endpoints (§5).
/// Parsing lives in <see cref="PlaylistResponseParser"/>; the create, add, and
/// library shapes were verified against real authenticated traffic (2026-07-22).
/// The playlist-items browse shape is only exercised on delete and remains the
/// one path not yet confirmed against a live capture.
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

        var playlistId = PlaylistResponseParser.ParseCreatedPlaylistId(root);
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

        var status = PlaylistResponseParser.ParseStatus(root);
        if (status is not null && !status.Contains("SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InnerTubeException($"edit_playlist add returned status {status}");
        }

        // Schema drift guard: adds reported success but no per-item results →
        // callers treat missing setVideoIds as non-undoable and fail loudly.
        return PlaylistResponseParser.ParseAddedItems(root);
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

        var status = PlaylistResponseParser.ParseStatus(root);
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

        var results = new List<PlaylistSummary>();
        PlaylistResponseParser.CollectLibraryPage(root, results);

        // Follow continuations so a large library isn't truncated (§11).
        var token = ContinuationPaging.FindToken(root);
        var pages = 0;
        while (token is not null && pages++ < MaxContinuationPages)
        {
            ct.ThrowIfCancellationRequested();
            var page = await transport.PostAsync(InnerTubeTransport.MusicOrigin, "browse",
                new Dictionary<string, object?> { ["continuation"] = token }, ct).ConfigureAwait(false);

            var before = results.Count;
            PlaylistResponseParser.CollectLibraryPage(page, results);
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
        PlaylistResponseParser.CollectPlaylistItemsPage(root, items);

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
            PlaylistResponseParser.CollectPlaylistItemsPage(page, items);
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

    private static string StripVl(string playlistId) =>
        playlistId.StartsWith("VL", StringComparison.Ordinal) ? playlistId[2..] : playlistId;
}
