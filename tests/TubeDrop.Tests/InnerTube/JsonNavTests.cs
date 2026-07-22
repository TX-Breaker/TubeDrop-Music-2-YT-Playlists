using System.Text.Json;
using TubeDrop.InnerTube.Http;
using TubeDrop.InnerTube.Json;

namespace TubeDrop.Tests.InnerTube;

public sealed class JsonNavTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Get_WalksObjectsAndArrays()
    {
        var root = Parse("""{"a":{"b":[{"c":"found"}]}}""");

        Assert.Equal("found", root.GetString("a", "b", 0, "c"));
    }

    [Theory]
    [InlineData("a", "missing")]
    [InlineData("a", "b", 5, "c")]
    [InlineData("a", "b", -1, "c")]
    [InlineData("a", "b", 0, "c", "deeper")]
    public void Get_MissingPath_ReturnsNull(params object[] path)
    {
        var root = Parse("""{"a":{"b":[{"c":"found"}]}}""");

        Assert.Null(root.Get(path));
    }

    [Fact]
    public void JoinRuns_ConcatenatesTexts()
    {
        var root = Parse("""{"title":{"runs":[{"text":"Hello "},{"text":"World"}]}}""");

        Assert.Equal("Hello World", root.JoinRuns("title"));
    }

    [Theory]
    [InlineData("3:47", 227)]
    [InlineData("0:59", 59)]
    [InlineData("1:02:03", 3723)]
    [InlineData("10:00", 600)]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("47", 0)]
    [InlineData("3:xx", 0)]
    [InlineData("1:2:3:4", 0)]
    public void ParseDurationSeconds(string? text, int expected)
    {
        Assert.Equal(expected, JsonNav.ParseDurationSeconds(text));
    }

    [Fact]
    public void Sanitize_StripsTrackingParams()
    {
        var root = Parse("""{"a":{"trackingParams":"x","keep":1},"list":[{"clickTrackingParams":"y","ok":true}]}""");

        var sanitized = InnerTubeTransport.Sanitize(root);

        Assert.DoesNotContain("trackingParams", sanitized);
        Assert.DoesNotContain("clickTrackingParams", sanitized);
        Assert.Contains("keep", sanitized);
        Assert.Contains("ok", sanitized);
    }
}
