namespace TubeDrop.InnerTube.Http;

/// <summary>
/// Global pacing for all InnerTube calls (§5): ~1 request / 700 ms with jitter.
/// Single instance shared by every client via DI.
/// </summary>
public sealed class InnerTubeRateLimiter(TimeSpan? baseInterval = null, int jitterMilliseconds = 250)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile TimeSpanBox _baseInterval = new(baseInterval ?? TimeSpan.FromMilliseconds(700));
    private readonly Random _random = new();
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    /// <summary>Boxed so the interval can be swapped atomically at runtime (rate-limit profile, §12).</summary>
    private sealed record TimeSpanBox(TimeSpan Value);

    /// <summary>Changes the pacing interval live (e.g. when the user picks a rate-limit profile).</summary>
    public void SetInterval(TimeSpan interval) => _baseInterval = new TimeSpanBox(interval);

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _nextAllowed)
            {
                await Task.Delay(_nextAllowed - now, cancellationToken).ConfigureAwait(false);
            }

            int jitter;
            lock (_random)
            {
                jitter = _random.Next(0, jitterMilliseconds);
            }

            _nextAllowed = DateTimeOffset.UtcNow + _baseInterval.Value + TimeSpan.FromMilliseconds(jitter);
        }
        finally
        {
            _gate.Release();
        }
    }
}
