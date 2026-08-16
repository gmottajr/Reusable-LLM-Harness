using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Contracts;

public sealed record LlmError(
    LlmErrorType Type,
    string Message,
    bool Retryable,
    string? Code = null);
