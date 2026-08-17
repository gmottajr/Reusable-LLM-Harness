using System.Net;
using System.Net.Http;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;

namespace LlmHarness.Core.Policies;

public static class LlmRetryClassifier
{
    public static bool IsRetryable(LlmError? error) =>
        error?.Retryable == true &&
        error.Type is not LlmErrorType.InputValidationError and
            not LlmErrorType.OutputValidationError;

    public static LlmError FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            LlmProviderException providerException => FromProviderException(providerException),
            TimeoutException => new LlmError(
                LlmErrorType.TimeoutError,
                "The LLM provider request timed out.",
                Retryable: true),
            TaskCanceledException => new LlmError(
                LlmErrorType.TimeoutError,
                "The LLM provider request was canceled by a timeout.",
                Retryable: true),
            OperationCanceledException => new LlmError(
                LlmErrorType.TimeoutError,
                "The LLM provider request was canceled by a timeout.",
                Retryable: true),
            HttpRequestException httpException => FromHttpRequestException(httpException),
            _ => new LlmError(
                LlmErrorType.UnknownError,
                "The LLM provider request failed.",
                Retryable: false)
        };
    }

    private static LlmError FromHttpRequestException(HttpRequestException exception)
    {
        if (exception.StatusCode is { } statusCode)
        {
            return FromProviderException(
                new LlmProviderException(
                    exception.Message,
                    (int)statusCode,
                    innerException: exception));
        }

        return new LlmError(
            LlmErrorType.ProviderError,
            "The LLM provider request failed due to a transient network error.",
            Retryable: true);
    }

    private static LlmError FromProviderException(LlmProviderException exception)
    {
        var statusCode = exception.StatusCode;
        var isServerError = statusCode is >= 500 and <= 599;
        var isRateLimited = statusCode == (int)HttpStatusCode.TooManyRequests;
        var isRequestTimeout = statusCode == (int)HttpStatusCode.RequestTimeout;
        var retryable = isRateLimited || isServerError || isRequestTimeout;

        var type = isRateLimited
            ? LlmErrorType.RateLimitError
            : LlmErrorType.ProviderError;

        var fallbackMessage = statusCode switch
        {
            (int)HttpStatusCode.TooManyRequests => "The LLM provider rate limit was exceeded.",
            >= 500 and <= 599 => "The LLM provider returned a transient server error.",
            (int)HttpStatusCode.RequestTimeout => "The LLM provider request timed out.",
            >= 400 and <= 499 => "The LLM provider rejected the request.",
            _ => "The LLM provider request failed."
        };

        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? fallbackMessage
            : exception.Message;

        return new LlmError(type, message, retryable, exception.ProviderCode);
    }
}
