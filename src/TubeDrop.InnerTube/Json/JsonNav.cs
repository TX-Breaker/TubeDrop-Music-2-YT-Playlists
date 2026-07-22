using System.Text.Json;

namespace TubeDrop.InnerTube.Json;

/// <summary>
/// Defensive JSON navigation (§5): YouTube renames/reshuffles nodes routinely,
/// so every access is a Try — a missing node yields null, never an exception.
/// </summary>
public static class JsonNav
{
    /// <summary>Walks a path of property names (string) and array indices (int).</summary>
    public static JsonElement? Get(this JsonElement element, params object[] path)
    {
        var current = element;
        foreach (var step in path)
        {
            switch (step)
            {
                case string name:
                    if (current.ValueKind != JsonValueKind.Object ||
                        !current.TryGetProperty(name, out var child))
                    {
                        return null;
                    }

                    current = child;
                    break;

                case int index:
                    if (current.ValueKind != JsonValueKind.Array ||
                        index < 0 || index >= current.GetArrayLength())
                    {
                        return null;
                    }

                    current = current[index];
                    break;

                default:
                    throw new ArgumentException($"Path steps must be string or int, got {step.GetType()}");
            }
        }

        return current;
    }

    public static string? GetString(this JsonElement element, params object[] path)
    {
        var node = element.Get(path);
        return node is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    }

    public static IEnumerable<JsonElement> GetArray(this JsonElement element, params object[] path)
    {
        var node = element.Get(path);
        if (node is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var item in array.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    /// <summary>Concatenates all <c>runs[].text</c> under the given path.</summary>
    public static string JoinRuns(this JsonElement element, params object[] path)
    {
        var parts = element.GetArray([.. path, "runs"])
            .Select(r => r.GetString("text"))
            .Where(t => t is not null);
        return string.Concat(parts);
    }

    /// <summary>Parses "m:ss" / "h:mm:ss" durations; 0 when unparseable.</summary>
    public static int ParseDurationSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return 0;
        }

        var seconds = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var value) || value < 0)
            {
                return 0;
            }

            seconds = seconds * 60 + value;
        }

        return seconds;
    }
}
