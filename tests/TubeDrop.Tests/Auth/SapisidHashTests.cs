using TubeDrop.InnerTube.Auth;

namespace TubeDrop.Tests.Auth;

public sealed class SapisidHashTests
{
    // Golden value computed independently (python hashlib):
    // sha1("1700000000 AbCdEfGhIjKlMnOpQr/st https://music.youtube.com")
    [Fact]
    public void ComputeAuthorizationHeader_MatchesIndependentImplementation()
    {
        var header = SapisidHash.ComputeAuthorizationHeader(
            "AbCdEfGhIjKlMnOpQr/st", "https://music.youtube.com", 1700000000);

        Assert.Equal("SAPISIDHASH 1700000000_f07ed4bdb33d8604b61a0521f86ec4d931a18a1b", header);
    }

    [Fact]
    public void ComputeAuthorizationHeader_DifferentOrigin_DifferentHash()
    {
        var music = SapisidHash.ComputeAuthorizationHeader("S", "https://music.youtube.com", 1);
        var web = SapisidHash.ComputeAuthorizationHeader("S", "https://www.youtube.com", 1);

        Assert.NotEqual(music, web);
    }

    [Fact]
    public void ComputeAuthorizationHeader_UsesTimeProvider()
    {
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1700000000));
        var header = SapisidHash.ComputeAuthorizationHeader(
            "AbCdEfGhIjKlMnOpQr/st", "https://music.youtube.com", time);

        Assert.StartsWith("SAPISIDHASH 1700000000_", header);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
