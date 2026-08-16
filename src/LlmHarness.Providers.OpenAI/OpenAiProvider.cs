using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Providers.OpenAI;

public sealed class OpenAiProvider : ILlmProvider, IProviderAvailabilityDetails
{
    public const string HttpClientName = "LlmHarness.OpenAI";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;

    public OpenAiProvider(
        IHttpClientFactory httpClientFactory,
        OpenAiOptions? options = null)
        : this(
            GetHttpClient(httpClientFactory),
            options)
    {
    }

    public OpenAiProvider(
        HttpClient httpClient,
        OpenAiOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new OpenAiOptions();
        _options.Validate();
    }

    public LlmProviderKind Kind => LlmProviderKind.OpenAI;

    public string? AvailabilityReason => _options.AvailabilityReason;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_options.IsConfigured);
    }

    public async Task<LlmProviderResponse> CompleteAsync(
        LlmProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.IsConfigured)
        {
            throw new LlmProviderException(
                _options.AvailabilityReason ?? "OpenAI provider is not configured.",
                statusCode: (int)HttpStatusCode.Unauthorized,
                providerCode: "missing_api_key");
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _options.Endpoint);
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Content = JsonContent.Create(
            CreateRequestBody(request),
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response.StatusCode, responseBody);
        }

        return ParseResponse(responseBody, request);
    }

    private static HttpClient GetHttpClient(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        return httpClientFactory.CreateClient(HttpClientName);
    }

    private OpenAiChatRequest CreateRequestBody(LlmProviderRequest request) =>
        new(
            request.Model ?? _options.DefaultModel,
            request.Messages.Select(message =>
                new OpenAiMessage(RoleName(message.Role), message.Content)).ToArray(),
            request.Temperature,
            request.MaxTokens);

    private static string RoleName(LlmMessageRole role) =>
        role switch
        {
            LlmMessageRole.System => "system",
            LlmMessageRole.User => "user",
            LlmMessageRole.Assistant => "assistant",
            LlmMessageRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported message role.")
        };

    private static LlmProviderResponse ParseResponse(
        string responseBody,
        LlmProviderRequest request)
    {
        OpenAiChatResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<OpenAiChatResponse>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new LlmProviderException(
                "OpenAI returned an invalid response payload.",
                providerCode: "invalid_response",
                innerException: exception);
        }

        var choice = response?.Choices?.FirstOrDefault();
        if (response is null || choice?.Message?.Content is null)
        {
            throw new LlmProviderException(
                "OpenAI returned no assistant content.",
                providerCode: "empty_response");
        }

        return new LlmProviderResponse(
            choice.Message.Content,
            LlmProviderKind.OpenAI,
            request.Model,
            choice.FinishReason,
            response.Id,
            response.Usage?.PromptTokens,
            response.Usage?.CompletionTokens);
    }

    private LlmProviderException CreateProviderException(
        HttpStatusCode statusCode,
        string responseBody)
    {
        var providerError = TryParseError(responseBody);
        return new LlmProviderException(
            Sanitize(providerError?.Message ?? "OpenAI returned an error response."),
            (int)statusCode,
            providerError?.Code);
    }

    private string Sanitize(string value) =>
        string.IsNullOrEmpty(_options.ApiKey)
            ? value
            : value.Replace(_options.ApiKey, "[REDACTED]", StringComparison.Ordinal);

    private static OpenAiError? TryParseError(string responseBody)
    {
        try
        {
            return JsonSerializer.Deserialize<OpenAiErrorEnvelope>(responseBody, JsonOptions)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record OpenAiChatRequest(
        string Model,
        IReadOnlyList<OpenAiMessage> Messages,
        double? Temperature,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens);

    private sealed record OpenAiMessage(string Role, string Content);

    private sealed class OpenAiChatResponse
    {
        public string? Id { get; init; }

        public List<OpenAiChoice>? Choices { get; init; }

        public OpenAiUsage? Usage { get; init; }
    }

    private sealed class OpenAiChoice
    {
        public OpenAiMessageContent? Message { get; init; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; init; }
    }

    private sealed class OpenAiMessageContent
    {
        public string? Content { get; init; }
    }

    private sealed class OpenAiUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; init; }
    }

    private sealed class OpenAiErrorEnvelope
    {
        public OpenAiError? Error { get; init; }
    }

    private sealed record OpenAiError(
        string? Message,
        string? Type,
        string? Code);
}
