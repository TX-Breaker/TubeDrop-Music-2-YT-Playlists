using System.Text.Json;

namespace TubeDrop.InnerTube.Json;

/// <summary>
/// Follows InnerTube continuation tokens so large playlists / libraries are not
/// truncated (§11 no silent truncation). YouTube exposes continuations in a few
/// interchangeable shapes; these finders search all of them defensively.
///
/// TODO(fixtures): the exact continuation shape for authenticated playlist/browse
/// responses needs a signed-in capture to lock down (§15). The finders below
/// target the documented shapes (continuationCommand.token /
/// nextContinuationData.continuation; appendContinuationItemsAction.continuationItems
/// / *Continuation.contents / gridContinuation.items) and are unit-tested against
/// synthetic JSON matching them.
/// </summary>
public static class ContinuationPaging
{
    /// <summary>Finds the next continuation token anywhere in the response, or null.</summary>
    public static string? FindToken(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                // Newer shape: { continuationCommand: { token: "..." } }
                var token = node.GetString("continuationCommand", "token");
                if (!string.IsNullOrEmpty(token))
                {
                    return token;
                }

                // Older shape: { nextContinuationData: { continuation: "..." } }
                token = node.GetString("nextContinuationData", "continuation");
                if (!string.IsNullOrEmpty(token))
                {
                    return token;
                }

                foreach (var property in node.EnumerateObject())
                {
                    if (FindToken(property.Value) is { } found)
                    {
                        return found;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    if (FindToken(item) is { } found)
                    {
                        return found;
                    }
                }

                break;
        }

        return null;
    }

    /// <summary>
    /// Yields every array of freshly-appended items in a continuation response:
    /// appendContinuationItemsAction.continuationItems, and the *Continuation
    /// (musicPlaylistShelfContinuation / gridContinuation / sectionListContinuation)
    /// contents/items arrays.
    /// </summary>
    public static IEnumerable<JsonElement> FindItemArrays(JsonElement node)
    {
        var results = new List<JsonElement>();
        Walk(node, results);
        return results;
    }

    private static void Walk(JsonElement node, List<JsonElement> results)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    var name = property.Name;
                    if (name == "continuationItems" && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        results.Add(property.Value);
                    }
                    else if (name.EndsWith("Continuation", StringComparison.Ordinal) &&
                             property.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (property.Value.Get("contents") is { ValueKind: JsonValueKind.Array } contents)
                        {
                            results.Add(contents);
                        }

                        if (property.Value.Get("items") is { ValueKind: JsonValueKind.Array } items)
                        {
                            results.Add(items);
                        }
                    }

                    Walk(property.Value, results);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    Walk(item, results);
                }

                break;
        }
    }
}
