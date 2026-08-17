using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LlmHarness.Core.Harness;

public static partial class LlmPayloadRedactor
{
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey",
        "api_key",
        "x-api-key",
        "x_goog_api_key",
        "authorization",
        "accessToken",
        "access_token",
        "refreshToken",
        "refresh_token",
        "clientSecret",
        "client_secret",
        "password",
        "secret",
        "cookie"
    };

    public static string? Redact(string? payload)
    {
        if (payload is null)
        {
            return null;
        }

        try
        {
            var json = JsonNode.Parse(payload);
            if (json is not null)
            {
                RedactNode(json);
                return json.ToJsonString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Provider output is not required to be JSON. Apply text redaction below.
        }

        var redacted = BearerTokenRegex().Replace(
            payload,
            match => $"{match.Groups["prefix"].Value}[REDACTED]");
        return SensitiveAssignmentRegex().Replace(
            redacted,
            match => $"{match.Groups["prefix"].Value}[REDACTED]");
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (SensitivePropertyNames.Contains(property.Key))
                {
                    jsonObject[property.Key] = "[REDACTED]";
                }
                else if (property.Value is not null)
                {
                    RedactNode(property.Value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    RedactNode(item);
                }
            }
        }
    }

    [GeneratedRegex(@"(?<prefix>\b(?:api[_-]?key|authorization|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|secret|cookie)\b\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;}]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(@"(?<prefix>\bBearer\s+)[^\s,;}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();
}
