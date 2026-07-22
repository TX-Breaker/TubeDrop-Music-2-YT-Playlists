using System.Text.Json;
using TubeDrop.InnerTube.Json;

namespace TubeDrop.Tests.InnerTube;

public sealed class ContinuationPagingTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void FindToken_NewShape_continuationCommand()
    {
        var root = Parse("""
            {"contents":{"stuff":[{"continuationItemRenderer":
              {"continuationEndpoint":{"continuationCommand":{"token":"TOKEN123"}}}}]}}
            """);

        Assert.Equal("TOKEN123", ContinuationPaging.FindToken(root));
    }

    [Fact]
    public void FindToken_OldShape_nextContinuationData()
    {
        var root = Parse("""
            {"continuations":[{"nextContinuationData":{"continuation":"OLDTOKEN"}}]}
            """);

        Assert.Equal("OLDTOKEN", ContinuationPaging.FindToken(root));
    }

    [Fact]
    public void FindToken_NoToken_ReturnsNull()
    {
        Assert.Null(ContinuationPaging.FindToken(Parse("""{"a":{"b":[1,2,3]}}""")));
    }

    [Fact]
    public void FindItemArrays_AppendContinuationItemsAction()
    {
        var root = Parse("""
            {"onResponseReceivedActions":[
              {"appendContinuationItemsAction":{"continuationItems":[{"x":1},{"x":2}]}}]}
            """);

        var arrays = ContinuationPaging.FindItemArrays(root).ToList();

        Assert.Single(arrays);
        Assert.Equal(2, arrays[0].GetArrayLength());
    }

    [Fact]
    public void FindItemArrays_ContinuationContentsShapes()
    {
        var root = Parse("""
            {"continuationContents":{
              "musicPlaylistShelfContinuation":{"contents":[{"a":1}]},
              "gridContinuation":{"items":[{"b":1},{"b":2}]}}}
            """);

        var arrays = ContinuationPaging.FindItemArrays(root).ToList();

        // one contents[] + one items[]
        Assert.Equal(2, arrays.Count);
        Assert.Contains(arrays, a => a.GetArrayLength() == 1);
        Assert.Contains(arrays, a => a.GetArrayLength() == 2);
    }

    [Fact]
    public void FindItemArrays_NoContinuation_Empty()
    {
        Assert.Empty(ContinuationPaging.FindItemArrays(Parse("""{"contents":{"foo":"bar"}}""")));
    }
}
