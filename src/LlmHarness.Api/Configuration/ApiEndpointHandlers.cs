using System.Text.Json;
using LlmHarness.Api.Mapping;
using LlmHarness.Api.Models;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Api.Configuration;

public static class ApiEndpointHandlers
{
    public static async Task<IResult> CompleteAsync(
        HttpRequest httpRequest,
        ILlmHarness harness,
        CancellationToken cancellationToken)
    {
        ApiLlmCompleteRequest? apiRequest;
        try
        {
            apiRequest = await httpRequest.ReadFromJsonAsync<ApiLlmCompleteRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest("Request body must contain valid JSON.", "request");
        }
        catch (NotSupportedException)
        {
            return BadRequest("Request body uses an unsupported JSON value.", "request");
        }
        catch (InvalidOperationException)
        {
            return BadRequest("Request content type must be application/json.", "request");
        }

        if (!ApiRequestMapper.TryMap(apiRequest, out var request, out var mappingError))
        {
            return BadRequest(mappingError!);
        }

        var mappedRequest = request!;
        if (mappedRequest.OutputSchema is not null)
        {
            var result = await harness.ExecuteAsync<JsonElement>(mappedRequest, cancellationToken);
            return ToHttpResult(result);
        }

        var textResult = await harness.ExecuteAsync<string>(mappedRequest, cancellationToken);
        return ToHttpResult(textResult);
    }

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
