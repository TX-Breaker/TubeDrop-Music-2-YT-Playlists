using System.Net.Http;
using System.Text.Json;

namespace TubeDrop.Core.Fingerprint;

public sealed record AcoustIdMatch(string Artist, string Title, double Score);

/// <summary>Looks up a Chromaprint fingerprint on the AcoustID web service.</summary>
public interface IAcoustIdClient
{
    Task<AcoustIdMatch?> LookupAsync(AudioFingerprint fingerprint, string apiKey, CancellationToken ct = default);
}

/// <summary>
/// AcoustID lookup (https://acoustid.org). Free API; the user supplies their own
/// application key. Returns the best-scoring recording's artist + title.
/// </summary>
public sealed class AcoustIdClient(HttpClient httpClient) : IAcoustIdClient
{
    private const string Endpoint = "https://api.acoustid.org/v2/lookup";

    public async Task<AcoustIdMatch?> LookupAsync(
        AudioFingerprint fingerprint, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrEmpty(fingerprint.Fingerprint))
        {
            return null;
        }

        try
        {
            var url = $"{Endpoint}?client={Uri.EscapeDataString(apiKey)}" +
                      $"&duration={fingerprint.DurationSeconds}" +
                      $"&fingerprint={Uri.EscapeDataString(fingerprint.Fingerprint)}" +
                      "&meta=recordings&format=json";

            using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return AcoustIdResponseParser.Parse(payload);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public static class AcoustIdResponseParser
{
    /// <summary>
    /// Parses an AcoustID lookup response, returning the highest-scoring result's
    /// first recording (title + first artist). Shape:
    /// { "status":"ok", "results":[ { "score":0.9, "recordings":[
    ///   { "title":"…", "artists":[ { "name":"…" } ] } ] } ] }.
    /// </summary>
    public static AcoustIdMatch? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            AcoustIdMatch? best = null;
            foreach (var result in results.EnumerateArray())
            {
                var score = result.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                    ? s.GetDouble()
                    : 0;
                if (!result.TryGetProperty("recordings", out var recordings) ||
                    recordings.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var recording in recordings.EnumerateArray())
                {
                    var title = recording.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                        ? t.GetString() ?? ""
                        : "";
                    var artist = "";
                    if (recording.TryGetProperty("artists", out var artists) &&
                        artists.ValueKind == JsonValueKind.Array && artists.GetArrayLength() > 0 &&
                        artists[0].TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    {
                        artist = n.GetString() ?? "";
                    }

                    if (title.Length > 0 && (best is null || score > best.Score))
                    {
                        best = new AcoustIdMatch(artist, title, score);
                    }
                }
            }

            return best;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
