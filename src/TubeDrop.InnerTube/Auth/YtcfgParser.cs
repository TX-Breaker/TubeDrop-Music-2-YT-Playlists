using System.Text.Json;

namespace TubeDrop.InnerTube.Auth;

public sealed record YtcfgValues(string ApiKey, JsonElement Context, string VisitorData);

/// <summary>
/// Parses the JSON blob extracted from a YouTube page via
/// <c>JSON.stringify({apiKey: ytcfg.get('INNERTUBE_API_KEY'), context: ytcfg.get('INNERTUBE_CONTEXT'), visitorData: ytcfg.get('VISITOR_DATA')})</c>.
/// Values are never hardcoded (§4.5) — this is the only source of key/context.
/// </summary>
public static class YtcfgParser
{
    public static YtcfgValues? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var apiKey = root.TryGetProperty("apiKey", out var keyEl) && keyEl.ValueKind == JsonValueKind.String
                ? keyEl.GetString()!
                : "";
            if (apiKey.Length == 0 ||
                !root.TryGetProperty("context", out var contextEl) ||
                contextEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var visitorData = root.TryGetProperty("visitorData", out var vdEl) && vdEl.ValueKind == JsonValueKind.String
                ? vdEl.GetString()!
                : "";

            return new YtcfgValues(apiKey, contextEl.Clone(), visitorData);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
