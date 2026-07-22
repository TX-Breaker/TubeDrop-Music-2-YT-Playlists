using System.Text.Json;
using TubeDrop.Core.Matching;
using TubeDrop.InnerTube.Json;

namespace TubeDrop.InnerTube.Search;

/// <summary>
/// Parsers for youtubei/v1/search responses. Coded strictly against captured
/// fixtures in tests/fixtures (§5, §15) — structures verified 2026-07-22:
///
/// YTM (WEB_REMIX): contents.tabbedSearchResultsRenderer.tabs[].tabRenderer
///   .content.sectionListRenderer.contents[].musicShelfRenderer.contents[]
///   .musicResponsiveListItemRenderer — flexColumns[0] = title + watchEndpoint
///   (videoId, musicVideoType), flexColumns[1].runs = artists/album/duration
///   separated by " • " runs, playlistItemData.videoId as fallback.
///
/// YT (WEB): contents.twoColumnSearchResultsRenderer.primaryContents
///   .sectionListRenderer.contents[].itemSectionRenderer.contents[]
///   .videoRenderer — title.runs, ownerText.runs, lengthText.simpleText,
///   badges[].metadataBadgeRenderer.label, ownerBadges[] style VERIFIED(_ARTIST).
/// </summary>
public static class SearchResponseParser
{
    public static IReadOnlyList<MatchCandidate> ParseYtmSearch(JsonElement root, CandidateSource source)
    {
        var results = new List<MatchCandidate>();

        var tabs = root.GetArray("contents", "tabbedSearchResultsRenderer", "tabs");
        foreach (var tab in tabs)
        {
            var sections = tab.GetArray("tabRenderer", "content", "sectionListRenderer", "contents");
            foreach (var section in sections)
            {
                foreach (var item in section.GetArray("musicShelfRenderer", "contents"))
                {
                    var candidate = ParseYtmItem(item, source);
                    if (candidate is not null)
                    {
                        results.Add(candidate);
                    }
                }
            }
        }

        return results;
    }

    private static MatchCandidate? ParseYtmItem(JsonElement item, CandidateSource source)
    {
        var renderer = item.Get("musicResponsiveListItemRenderer");
        if (renderer is not { } r)
        {
            return null;
        }

        var titleRun = r.Get("flexColumns", 0, "musicResponsiveListItemFlexColumnRenderer", "text", "runs", 0);
        var title = titleRun?.GetString("text");
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var videoId = titleRun?.GetString("navigationEndpoint", "watchEndpoint", "videoId")
                      ?? r.GetString("playlistItemData", "videoId");
        if (string.IsNullOrEmpty(videoId))
        {
            return null;
        }

        var musicVideoType = titleRun?.GetString(
            "navigationEndpoint", "watchEndpoint",
            "watchEndpointMusicSupportedConfigs", "watchEndpointMusicConfig", "musicVideoType") ?? "";

        var artists = new List<string>();
        var album = "";
        var duration = 0;
        foreach (var run in r.GetArray("flexColumns", 1, "musicResponsiveListItemFlexColumnRenderer", "text", "runs"))
        {
            var text = run.GetString("text");
            if (string.IsNullOrWhiteSpace(text) || text.Trim() is "•" or "·")
            {
                continue;
            }

            var pageType = run.GetString(
                "navigationEndpoint", "browseEndpoint",
                "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig", "pageType");
            if (pageType == "MUSIC_PAGE_TYPE_ARTIST")
            {
                artists.Add(text.Trim());
            }
            else if (pageType == "MUSIC_PAGE_TYPE_ALBUM")
            {
                album = text.Trim();
            }
            else if (JsonNav.ParseDurationSeconds(text) is var parsed and > 0)
            {
                duration = parsed;
            }
            else if (artists.Count == 0 && pageType is null && !text.Contains(" views") && !text.Contains(" plays"))
            {
                // Unlinked artist name (common for videos shelf).
                artists.Add(text.Trim());
            }
        }

        return new MatchCandidate
        {
            VideoId = videoId,
            Title = title,
            Artists = artists,
            Album = album,
            DurationSeconds = duration,
            Channel = artists.FirstOrDefault() ?? "",
            Source = source,
            // ATV = official song entry in YTM ("song" result, spec authority bonus)
            IsOfficialArtistChannel = musicVideoType == "MUSIC_VIDEO_TYPE_ATV",
            Badges = musicVideoType.Length > 0 ? [musicVideoType] : [],
        };
    }

    public static IReadOnlyList<MatchCandidate> ParseYouTubeSearch(JsonElement root)
    {
        var results = new List<MatchCandidate>();

        var sections = root.GetArray(
            "contents", "twoColumnSearchResultsRenderer", "primaryContents",
            "sectionListRenderer", "contents");
        foreach (var section in sections)
        {
            foreach (var item in section.GetArray("itemSectionRenderer", "contents"))
            {
                if (item.Get("videoRenderer") is not { } v)
                {
                    continue;
                }

                var videoId = v.GetString("videoId");
                var title = v.JoinRuns("title");
                if (string.IsNullOrEmpty(videoId) || string.IsNullOrEmpty(title))
                {
                    continue;
                }

                var channel = v.JoinRuns("ownerText");
                var badges = v.GetArray("badges")
                    .Select(b => b.GetString("metadataBadgeRenderer", "label"))
                    .Where(l => !string.IsNullOrEmpty(l))
                    .Select(l => l!)
                    .ToList();
                var verified = v.GetArray("ownerBadges")
                    .Select(b => b.GetString("metadataBadgeRenderer", "style"))
                    .Any(s => s is "BADGE_STYLE_TYPE_VERIFIED_ARTIST" or "BADGE_STYLE_TYPE_VERIFIED");

                results.Add(new MatchCandidate
                {
                    VideoId = videoId!,
                    Title = title,
                    Artists = channel.Length > 0 ? [channel] : [],
                    Channel = channel,
                    DurationSeconds = JsonNav.ParseDurationSeconds(v.GetString("lengthText", "simpleText")),
                    Badges = badges,
                    Source = CandidateSource.YouTube,
                    IsOfficialArtistChannel = verified,
                });
            }
        }

        return results;
    }
}
