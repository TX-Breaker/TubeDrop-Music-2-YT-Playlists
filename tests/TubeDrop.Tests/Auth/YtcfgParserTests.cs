using System.Text.Json;
using TubeDrop.InnerTube.Auth;

namespace TubeDrop.Tests.Auth;

public sealed class YtcfgParserTests
{
    [Fact]
    public void Parse_ValidBlob_ExtractsAll()
    {
        const string json = """
            {
              "apiKey": "AIzaSyTest123",
              "context": { "client": { "clientName": "WEB_REMIX", "clientVersion": "1.20260101.01.00" } },
              "visitorData": "CgtViSItor",
              "sessionIndex": "1"
            }
            """;

        var result = YtcfgParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("AIzaSyTest123", result.ApiKey);
        Assert.Equal("CgtViSItor", result.VisitorData);
        Assert.Equal("WEB_REMIX",
            result.Context.GetProperty("client").GetProperty("clientName").GetString());
    }

    [Fact]
    public void Parse_ContextSurvivesDocumentDisposal()
    {
        var result = YtcfgParser.Parse("""{"apiKey":"k","context":{"client":{}},"visitorData":""}""");

        Assert.NotNull(result);
        // Clone() must make the element independent of the parsed document.
        Assert.Equal(JsonValueKind.Object, result.Context.GetProperty("client").ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"apiKey":null,"context":null}""")]
    [InlineData("""{"apiKey":"","context":{}}""")]
    [InlineData("""{"apiKey":"k","context":"not-an-object"}""")]
    public void Parse_InvalidBlob_ReturnsNull(string? json)
    {
        Assert.Null(YtcfgParser.Parse(json));
    }

    [Fact]
    public void Parse_MissingVisitorData_DefaultsEmpty()
    {
        var result = YtcfgParser.Parse("""{"apiKey":"k","context":{}}""");

        Assert.NotNull(result);
        Assert.Equal("", result.VisitorData);
    }
}
