using LlmHarness.Api.Models;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;

namespace LlmHarness.Api.Mapping;

public static class ApiResponseMapper
{
    public static ApiLlmResultResponse<TData> Map<TData>(LlmResult<TData> result)
    {
        var error = result.Error is null
            ? null
            : new ApiErrorResponse(
                result.Error.Type.ToString(),
                result.Error.Message,
                result.Error.Retryable,
                result.Error.Code);
        var metadata = new ApiMetadataResponse(
            (result.Metadata.SelectedProvider ?? result.Metadata.Provider)?.ToString(),
            result.Metadata.Model,
            result.Metadata.AttemptCount,
            result.Metadata.RetryCount,
            result.Metadata.Duration is { } duration
                ? Math.Round(duration.TotalMilliseconds, 2)
                : null,
            result.Metadata.TimeoutMs,
            result.Metadata.FallbackUsed,
            result.Metadata.CorrelationId);

        return result.Success
            ? new ApiLlmResultResponse<TData>(true, result.Output, null, metadata)
            : new ApiLlmResultResponse<TData>(false, null, error, metadata);
    }

    public static int StatusCode<TData>(LlmResult<TData> result) =>
        result.Error?.Type switch
        {
            LlmErrorType.InputValidationError => StatusCodes.Status400BadRequest,
            LlmErrorType.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
            LlmErrorType.RateLimitError => StatusCodes.Status429TooManyRequests,
            LlmErrorType.TimeoutError => StatusCodes.Status504GatewayTimeout,
            LlmErrorType.ProviderError or
            LlmErrorType.OutputValidationError or
            LlmErrorType.OutputParsingError or
            LlmErrorType.SerializationError => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        };
}
