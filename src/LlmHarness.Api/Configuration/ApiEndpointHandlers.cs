using System.Text.Json;
using LlmHarness.Api.Mapping;
using LlmHarness.Api.Models;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LlmHarness.Api.Configuration;

public static class ApiEndpointHandlers
{
    public static async Task<IResult> CompleteAsync(
        HttpRequest httpRequest,
        ILlmHarness harness,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("LlmHarness.Api.Completion");
        logger.LogInformation(
            "CompletionRequestReceived method={Method} path={Path} traceId={TraceId}",
            httpRequest.Method,
            httpRequest.Path,
            httpRequest.HttpContext.TraceIdentifier);

        ApiLlmCompleteRequest? apiRequest;
        try
        {
            apiRequest = await httpRequest.ReadFromJsonAsync<ApiLlmCompleteRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            logger.LogWarning("CompletionRequestRejected reason=InvalidJson traceId={TraceId}", httpRequest.HttpContext.TraceIdentifier);
            return BadRequest("Request body must contain valid JSON.", "request");
        }
        catch (NotSupportedException)
        {
            logger.LogWarning("CompletionRequestRejected reason=UnsupportedJsonValue traceId={TraceId}", httpRequest.HttpContext.TraceIdentifier);
            return BadRequest("Request body uses an unsupported JSON value.", "request");
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("CompletionRequestRejected reason=InvalidContentType traceId={TraceId}", httpRequest.HttpContext.TraceIdentifier);
            return BadRequest("Request content type must be application/json.", "request");
        }

        if (!ApiRequestMapper.TryMap(apiRequest, out var request, out var mappingError))
        {
            logger.LogWarning(
                "CompletionRequestRejected reason=Validation code={Code} traceId={TraceId}",
                mappingError?.Code,
                httpRequest.HttpContext.TraceIdentifier);
            return BadRequest(mappingError!);
        }

        var mappedRequest = request!;
        logger.LogInformation(
            "CompletionMapped provider={Provider} model={Model} messageCount={MessageCount} hasOutputSchema={HasOutputSchema} traceId={TraceId}",
            mappedRequest.Provider,
            mappedRequest.Model,
            mappedRequest.Messages.Count,
            mappedRequest.OutputSchema is not null,
            httpRequest.HttpContext.TraceIdentifier);

        if (mappedRequest.OutputSchema is not null)
        {
            var result = await harness.ExecuteAsync<JsonElement>(mappedRequest, cancellationToken);
            LogCompletionResult(logger, result.Success, result.Error?.Type.ToString(), result.Metadata, httpRequest.HttpContext.TraceIdentifier);
            return ToHttpResult(result);
        }

        var textResult = await harness.ExecuteAsync<string>(mappedRequest, cancellationToken);
        LogCompletionResult(logger, textResult.Success, textResult.Error?.Type.ToString(), textResult.Metadata, httpRequest.HttpContext.TraceIdentifier);
        return ToHttpResult(textResult);
    }

    private static void LogCompletionResult(
        ILogger logger,
        bool success,
        string? errorType,
        LlmMetadata metadata,
        string traceId) =>
        logger.LogInformation(
            "CompletionFinished success={Success} errorType={ErrorType} provider={Provider} model={Model} attempts={Attempts} retryCount={RetryCount} durationMs={DurationMs} correlationId={CorrelationId} traceId={TraceId}",
            success,
            errorType,
            metadata.SelectedProvider ?? metadata.Provider,
            metadata.Model,
            metadata.AttemptCount,
            metadata.RetryCount,
            metadata.Duration?.TotalMilliseconds,
            metadata.CorrelationId,
            traceId);

    private static IResult BadRequest(string message, string code) =>
        BadRequest(new ApiErrorResponse(
            "InputValidationError",
            message,
            Retryable: false,
            code));

    private static IResult BadRequest(ApiErrorResponse error)
    {
        var response = new ApiLlmResultResponse<object?>(
            false,
            null,
            error,
            new ApiMetadataResponse());
        return Results.Json(response, statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult ToHttpResult<TData>(LlmResult<TData> result)
    {
        var statusCode = result.Success
            ? StatusCodes.Status200OK
            : ApiResponseMapper.StatusCode(result);
        return Results.Json(ApiResponseMapper.Map(result), statusCode: statusCode);
    }
}
