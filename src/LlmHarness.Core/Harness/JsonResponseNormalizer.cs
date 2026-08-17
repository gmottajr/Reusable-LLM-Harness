using System.Buffers;
using System.Text;
using System.Text.Json;

namespace LlmHarness.Core.Harness;

/// <summary>
/// Extracts the first valid JSON value from an LLM response.
/// Models commonly wrap JSON in Markdown fences or a short explanation.
/// </summary>
internal static class JsonResponseNormalizer
{
    private static readonly SearchValues<char> JsonValueStartCharacters =
        SearchValues.Create("[{\"-0123456789tfn");

    public static bool TryParse(string content, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var candidate = RemoveMarkdownFence(content);
        if (TryParseWhole(candidate, out value))
        {
            return true;
        }

        // If the model added prose, try each possible JSON value boundary.
        // JsonDocument.ParseValue handles objects, arrays, strings, numbers,
        // booleans, and null uniformly.
        for (var index = candidate.AsSpan().IndexOfAny(JsonValueStartCharacters); index >= 0;)
        {
            var absoluteIndex = index;
            if (TryParseValue(candidate[absoluteIndex..], out value))
            {
                return true;
            }

            var nextIndex = absoluteIndex + 1;
            var remaining = candidate[nextIndex..].AsSpan();
            var relativeIndex = remaining.IndexOfAny(JsonValueStartCharacters);
            if (relativeIndex < 0)
            {
                break;
            }

            index = nextIndex + relativeIndex;
        }

        return false;
    }

    private static bool TryParseWhole(string content, out JsonElement value)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryParseValue(string content, out JsonElement value)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
            using var document = JsonDocument.ParseValue(ref reader);
            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static string RemoveMarkdownFence(string content)
    {
        var trimmed = content.Trim().TrimStart('\uFEFF');
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return trimmed;
        }

        var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence <= firstLineEnd)
        {
            return trimmed[(firstLineEnd + 1)..].Trim();
        }

        return trimmed[(firstLineEnd + 1)..closingFence].Trim();
    }
}
