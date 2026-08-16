using System.Text.Json;

namespace LlmHarness.Api.Models;

public sealed class ApiLlmCompleteRequest
{
    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? ExecutionMode { get; init; }

    public long? TimeoutMs { get; init; }

    public double? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public List<ApiLlmMessage?>? Messages { get; init; }

    public JsonElement? OutputSchema { get; init; }
}
