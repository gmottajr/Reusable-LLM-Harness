using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Harness;

public sealed record LlmLogEvent
{
    public required string CorrelationId { get; init; }

    public required string Status { get; init; }

    public LlmProviderKind? Provider { get; init; }

    public string? Model { get; init; }

    public TimeSpan? Duration { get; init; }

    public int Attempts { get; init; }

    public int RetryCount { get; init; }

    public bool? Success { get; init; }

    public LlmErrorType? ErrorType { get; init; }

    public bool FallbackUsed { get; init; }
}
