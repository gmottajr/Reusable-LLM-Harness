namespace LlmHarness.Api.Models;

public sealed record ApiErrorResponse(
    string Type,
    string Message,
    bool Retryable,
    string? Code = null);

public sealed record ApiMetadataResponse(
    string? Provider = null,
    string? Model = null,
    int Attempts = 0,
    int RetryCount = 0,
    double? DurationMs = null,
    long? TimeoutMs = null,
    bool FallbackUsed = false,
    string? CorrelationId = null);

public sealed record ApiLlmResultResponse<TData>(
    bool Success,
    TData? Data,
    ApiErrorResponse? Error,
    ApiMetadataResponse Metadata);

public sealed record ApiProviderStatusResponse(
    string Provider,
    bool Available,
    string? Reason);
