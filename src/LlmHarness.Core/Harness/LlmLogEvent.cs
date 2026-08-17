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

    // Raw and normalized fields support output-validation diagnostics. Request
    // and response payloads are available to the configured diagnostic logger.
    public string? RawResponse { get; init; }

    public string? NormalizedResponse { get; init; }

    public string? OutputSchema { get; init; }

    public string? ValidationPath { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorCode { get; init; }

    public string? RequestPayload { get; init; }

    public string? ResponsePayload { get; init; }
}
