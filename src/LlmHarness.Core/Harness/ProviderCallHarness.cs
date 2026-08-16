using System.Text.Json;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Policies;

namespace LlmHarness.Core.Harness;

internal sealed class ProviderCallHarness : ILlmHarness
{
    private readonly ILlmProvider _provider;

    public ProviderCallHarness(ILlmProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        LlmProviderResponse response;
        try
        {
            response = await _provider.CompleteAsync(
                new LlmProviderRequest(
                    request.Messages,
                    _provider.Kind,
                    request.Model,
                    request.HasTimeoutOverride ? request.Timeout : null,
                    request.Temperature,
                    request.MaxTokens,
                    request.OutputSchema),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LlmResult<TOutput>.CreateFailure(
                LlmRetryClassifier.FromException(exception),
                ProviderMetadata(request));
        }

        return ConvertResponse<TOutput>(response, request);
    }

    private LlmResult<TOutput> ConvertResponse<TOutput>(
        LlmProviderResponse response,
        LlmRequest request)
    {
        if (response.Content is null)
        {
            return LlmResult<TOutput>.CreateFailure(
                new LlmError(
                    LlmErrorType.SerializationError,
                    "The provider returned an empty response.",
                    Retryable: false),
                ProviderMetadata(request, response));
        }

        try
        {
            var output = Deserialize<TOutput>(response.Content);
            return LlmResult<TOutput>.CreateSuccess(
                output!,
                ProviderMetadata(request, response));
        }
        catch (JsonException)
        {
            return LlmResult<TOutput>.CreateFailure(
                new LlmError(
                    LlmErrorType.SerializationError,
                    "The provider response could not be converted to the requested output type.",
                    Retryable: false),
                ProviderMetadata(request, response));
        }
        catch (NotSupportedException)
        {
            return LlmResult<TOutput>.CreateFailure(
                new LlmError(
                    LlmErrorType.SerializationError,
                    "The requested output type is not supported.",
                    Retryable: false),
                ProviderMetadata(request, response));
        }
    }

    private static TOutput? Deserialize<TOutput>(string content)
    {
        if (typeof(TOutput) == typeof(string))
        {
            return (TOutput)(object)content;
        }

        if (typeof(TOutput) == typeof(JsonElement))
        {
            using var document = JsonDocument.Parse(content);
            return (TOutput)(object)document.RootElement.Clone();
        }

        return JsonSerializer.Deserialize<TOutput>(content);
    }

    private LlmMetadata ProviderMetadata(
        LlmRequest request,
        LlmProviderResponse? response = null) =>
        new()
        {
            Provider = _provider.Kind,
            SelectedProvider = _provider.Kind,
            Model = response?.Model ?? request.Model,
            RequestId = response?.ProviderRequestId,
            CompletedAt = DateTimeOffset.UtcNow
        };
}
