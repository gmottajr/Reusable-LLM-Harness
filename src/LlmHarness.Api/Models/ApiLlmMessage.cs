namespace LlmHarness.Api.Models;

public sealed class ApiLlmMessage
{
    public string? Role { get; init; }

    public string? Content { get; init; }
}
