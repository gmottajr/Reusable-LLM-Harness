using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Contracts;

public sealed record LlmMetadata
{
    public LlmProviderKind? Provider { get; init; }

    public LlmProviderKind? SelectedProvider { get; init; }

    public string? Model { get; init; }

    public int AttemptCount { get; init; }

    public int RetryCount { get; init; }

    public TimeSpan? Duration { get; init; }

    public long? TimeoutMs { get; init; }

    public bool FallbackUsed { get; init; }

    public string? CorrelationId { get; init; }

    public string? RequestId { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Raw provider content retained for diagnostic logging.
    /// </summary>
    public string? RawResponse { get; init; }
}
