using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Settings;

namespace TubeDrop.Core.Matching.Refiners;

/// <summary>
/// Third rung of the ladder (§8.3), default OFF: asks a cloud LLM (Anthropic-style
/// messages endpoint) to rewrite a noisy track into clean search queries. The API
/// key comes from user settings and is never stored in the repo. Any failure
/// degrades to "no extra queries" — never blocks the batch (§15).
/// </summary>
public sealed class CloudQueryRefiner(
    HttpClient httpClient,
    ISettingsStore settings,
    ILogger<CloudQueryRefiner> logger) : IQueryRefiner
{
    private const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";

    public string Name => "cloud";

    public async Task<IReadOnlyList<string>> RefineAsync(TrackInfo track, CancellationToken ct = default)
    {
        var s = settings.Current;
        if (!s.CloudRefinerEnabled || string.IsNullOrWhiteSpace(s.CloudRefinerApiKey))
        {
            return [];
        }

        try
        {
            var prompt =
                "You clean up messy music metadata into YouTube search queries. " +
                $"Artist: \"{track.Artist}\". Title: \"{track.Title}\". " +
                "Return 1-3 concise search queries, one per line, no numbering, no commentary.";

            var body = new
            {
                model = s.CloudRefinerModel,
                max_tokens = 200,
                messages = new[] { new { role = "user", content = prompt } },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, DefaultEndpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("x-api-key", s.CloudRefinerApiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cloud refiner HTTP {Status}", (int)response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseQueries(payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cloud refiner failed — skipping");
            return [];
        }
    }

    /// <summary>Extracts the model's text and splits it into query lines. Parses the Anthropic messages response shape.</summary>
    internal static IReadOnlyList<string> ParseQueries(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                    block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    sb.AppendLine(text.GetString());
                }
            }

            return sb.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
