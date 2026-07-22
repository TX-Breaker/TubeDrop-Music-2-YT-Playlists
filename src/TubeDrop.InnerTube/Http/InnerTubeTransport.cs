using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TubeDrop.InnerTube.Auth;

namespace TubeDrop.InnerTube.Http;

public sealed class InnerTubeException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>Thrown when the circuit breaker is open — callers surface the friendly "YouTube changed something" message.</summary>
public sealed class InnerTubeCircuitOpenException() : Exception("InnerTube circuit breaker is open");

/// <summary>
/// Single HTTP door to youtubei/v1 (§5): SAPISIDHASH auth headers, global rate
/// limiting, exponential backoff on 429/5xx, circuit breaker, optional capture
/// mode that dumps sanitized responses for fixtures. Cookies/auth are never logged.
/// </summary>
public sealed class InnerTubeTransport
{
    public const string MusicOrigin = "https://music.youtube.com";
    public const string WebOrigin = "https://www.youtube.com";

    private const int MaxAttempts = 4;
    private const int CircuitThreshold = 5;
    private static readonly TimeSpan CircuitCooldown = TimeSpan.FromMinutes(2);

    private readonly HttpClient _httpClient;
    private readonly InnerTubeRateLimiter _rateLimiter;
    private readonly ISessionProvider _sessionProvider;
    private readonly ILogger<InnerTubeTransport> _logger;

    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenUntil = DateTimeOffset.MinValue;

    /// <summary>When set, every response body is dumped (sanitized) to this directory.</summary>
    public string? CaptureDirectory { get; set; }

    public InnerTubeTransport(
        HttpClient httpClient,
        InnerTubeRateLimiter rateLimiter,
        ISessionProvider sessionProvider,
        ILogger<InnerTubeTransport> logger)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _sessionProvider = sessionProvider;
        _logger = logger;
    }

    /// <summary>POSTs to {origin}/youtubei/v1/{endpoint} and returns the parsed response root.</summary>
    public async Task<JsonElement> PostAsync(
        string origin,
        string endpoint,
        Dictionary<string, object?> body,
        CancellationToken cancellationToken = default)
    {
        if (DateTimeOffset.UtcNow < _circuitOpenUntil)
        {
            throw new InnerTubeCircuitOpenException();
        }

        var session = _sessionProvider.Current
                      ?? throw new InnerTubeException("Not signed in");

        var isMusic = origin == MusicOrigin;
        var apiKey = isMusic ? session.MusicApiKey : session.WebApiKey;
        var context = isMusic ? session.MusicContext : session.WebContext;
        if (string.IsNullOrEmpty(apiKey) || context is null)
        {
            throw new InnerTubeException($"No ytcfg captured for {origin}");
        }

        body["context"] = context;
        var json = JsonSerializer.Serialize(body);
        var url = $"{origin}/youtubei/v1/{endpoint}?key={apiKey}&prettyPrint=false";

        for (var attempt = 1; ; attempt++)
        {
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Authorization",
                SapisidHash.ComputeAuthorizationHeader(session.Sapisid, origin, TimeProvider.System));
            request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
            request.Headers.TryAddWithoutValidation("Origin", origin);
            request.Headers.TryAddWithoutValidation("X-Origin", origin);
            request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", session.AuthUser);
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
            if (session.VisitorData.Length > 0)
            {
                request.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", session.VisitorData);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning("InnerTube {Endpoint} network error (attempt {Attempt}): {Message}",
                    endpoint, attempt, ex.Message);
                await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("InnerTube {Endpoint}: {Status} — session expired", endpoint, (int)response.StatusCode);
                    _sessionProvider.NotifyExpired();
                    throw new InnerTubeException("Session expired", response.StatusCode);
                }

                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (attempt < MaxAttempts)
                    {
                        _logger.LogWarning("InnerTube {Endpoint}: {Status} (attempt {Attempt}) — backing off",
                            endpoint, (int)response.StatusCode, attempt);
                        await BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    RecordFailure();
                    throw new InnerTubeException($"InnerTube {endpoint} failed after {MaxAttempts} attempts",
                        response.StatusCode);
                }

                if (!response.IsSuccessStatusCode)
                {
                    RecordFailure();
                    throw new InnerTubeException($"InnerTube {endpoint}: HTTP {(int)response.StatusCode}",
                        response.StatusCode);
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _consecutiveFailures = 0;

                JsonElement root;
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    root = doc.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    RecordFailure();
                    throw new InnerTubeException($"InnerTube {endpoint}: unparseable response", null, ex);
                }

                Capture(endpoint, root);
                return root;
            }
        }
    }

    private static Task BackoffAsync(int attempt, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);

    private void RecordFailure()
    {
        if (++_consecutiveFailures >= CircuitThreshold)
        {
            _circuitOpenUntil = DateTimeOffset.UtcNow + CircuitCooldown;
            _consecutiveFailures = 0;
            _logger.LogError("InnerTube circuit breaker OPEN for {Cooldown}", CircuitCooldown);
        }
    }

    private void Capture(string endpoint, JsonElement root)
    {
        var dir = CaptureDirectory;
        if (dir is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(dir);
            var name = $"{endpoint.Replace('/', '_')}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}.json";
            File.WriteAllText(Path.Combine(dir, name), Sanitize(root));
            _logger.LogInformation("Captured {Endpoint} response to {Name}", endpoint, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fixture capture failed");
        }
    }

    /// <summary>Removes tracking params — same rule used for committed fixtures.</summary>
    internal static string Sanitize(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteSanitized(root, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSanitized(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "trackingParams" or "clickTrackingParams")
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteSanitized(property.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteSanitized(item, writer);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
