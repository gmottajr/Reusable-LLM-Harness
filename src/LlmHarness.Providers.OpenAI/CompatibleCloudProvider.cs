using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Providers.OpenAI;

public sealed class CompatibleCloudProvider : ILlmProvider, IProviderAvailabilityDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly CompatibleCloudProviderOptions _options;

    public CompatibleCloudProvider(
        HttpClient httpClient,
        CompatibleCloudProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public LlmProviderKind Kind => _options.Provider;

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
                _options.AvailabilityReason ?? $"{Kind} provider is not configured.",
                statusCode: (int)HttpStatusCode.Unauthorized,
                providerCode: "missing_api_key");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
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

    private OpenAiChatRequest CreateRequestBody(LlmProviderRequest request) =>
        new(
            request.Model ?? _options.DefaultModel,
            request.Messages.Select(message =>
                new OpenAiMessage(RoleName(message.Role), message.Content)).ToArray(),
            request.Temperature,
            request.MaxTokens,
            request.OutputSchema is null ? null : new OpenAiResponseFormat("json_object"));

    private static string RoleName(LlmMessageRole role) =>
        role switch
        {
            LlmMessageRole.System => "system",
            LlmMessageRole.User => "user",
            LlmMessageRole.Assistant => "assistant",
            LlmMessageRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported message role.")
        };

    private LlmProviderResponse ParseResponse(string responseBody, LlmProviderRequest request)
    {
        OpenAiChatResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<OpenAiChatResponse>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new LlmProviderException(
                $"{Kind} returned an invalid response payload.",
                providerCode: "invalid_response",
                innerException: exception);
        }

        var choice = response?.Choices?.FirstOrDefault();
        if (response is null || choice?.Message?.Content is null)
        {
            throw new LlmProviderException(
                $"{Kind} returned no assistant content.",
                providerCode: "empty_response");
        }

        return new LlmProviderResponse(
            choice.Message.Content,
            Kind,
            request.Model ?? _options.DefaultModel,
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
        var message = providerError.Message ?? $"{Kind} returned an error response.";
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            message = message.Replace(_options.ApiKey, "[REDACTED]", StringComparison.Ordinal);
        }

        return new LlmProviderException(message, (int)statusCode, providerError.Code);
    }

    private static (string? Message, string? Code) TryParseError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var message = ReadString(root, "message");
            var code = ReadString(root, "code");

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    message ??= error.GetString();
                }
                else if (error.ValueKind == JsonValueKind.Object)
                {
                    message ??= ReadString(error, "message");
                    code ??= ReadString(error, "code");
                }
            }

            return (message, code);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record OpenAiChatRequest(
        string Model,
        IReadOnlyList<OpenAiMessage> Messages,
        double? Temperature,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens,
        [property: JsonPropertyName("response_format")] OpenAiResponseFormat? ResponseFormat);

    private sealed record OpenAiResponseFormat(string Type);

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

}
