namespace LlmHarness.Api.Logging;

public sealed record LlmLoggingOptions
{
    public bool IncludePayloads { get; init; } = true;
}
