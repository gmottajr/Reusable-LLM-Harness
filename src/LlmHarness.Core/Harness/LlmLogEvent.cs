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

    // Populated only for output-validation diagnostics. These fields are
    // intentionally kept out of normal flow events unless a schema mismatch
    // needs investigation.
    public string? RawResponse { get; init; }

    public string? NormalizedResponse { get; init; }

    public string? OutputSchema { get; init; }

    public string? ValidationPath { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorCode { get; init; }
}
