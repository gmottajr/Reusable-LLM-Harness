namespace LlmHarness.Core.Enums;

public enum LlmErrorType
{
    None,
    InputValidationError,
    ProviderUnavailable,
    ProviderError,
    RateLimitError,
    TimeoutError,
    OutputValidationError,
    SerializationError,
    UnknownError
}
