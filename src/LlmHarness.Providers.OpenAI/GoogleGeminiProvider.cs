using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Providers.OpenAI;

public sealed class GoogleGeminiProvider : ILlmProvider, IProviderAvailabilityDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly GoogleGeminiOptions _options;

    public GoogleGeminiProvider(HttpClient httpClient, GoogleGeminiOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public LlmProviderKind Kind => LlmProviderKind.GoogleGemini;

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
                _options.AvailabilityReason ?? "Google Gemini provider is not configured.",
                statusCode: (int)HttpStatusCode.Unauthorized,
                providerCode: "missing_api_key");
        }

        var model = request.Model ?? _options.DefaultModel;
        var endpoint = $"{_options.Endpoint.TrimEnd('/')}/models/{Uri.EscapeDataString(model)}:generateContent";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Add("X-goog-api-key", _options.ApiKey);
        httpRequest.Content = JsonContent.Create(CreateRequestBody(request), options: JsonOptions);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response.StatusCode, responseBody);
        }

        return ParseResponse(responseBody, request, model);
    }

    private static GeminiRequest CreateRequestBody(LlmProviderRequest request)
    {
        var systemMessages = request.Messages
            .Where(message => message.Role == LlmMessageRole.System)
            .Select(message => message.Content)
            .ToArray();
        var contents = request.Messages
            .Where(message => message.Role != LlmMessageRole.System)
            .Select(message => new GeminiContent(
                message.Role == LlmMessageRole.Assistant ? "model" : "user",
                [new GeminiPart(message.Content)]))
            .ToArray();

        var generationConfig = request.OutputSchema is null
            ? null
            : new GeminiGenerationConfig("application/json");

        return new GeminiRequest(
            contents,
            systemMessages.Length == 0
                ? null
                : new GeminiSystemInstruction([new GeminiPart(string.Join("\n", systemMessages))]),
            generationConfig);
    }

    private static LlmProviderResponse ParseResponse(
        string responseBody,
        LlmProviderRequest request,
        string model)
    {
        GeminiResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<GeminiResponse>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new LlmProviderException(
                "Google Gemini returned an invalid response payload.",
                providerCode: "invalid_response",
                innerException: exception);
        }

        var content = response?.Candidates?.FirstOrDefault()?.Content?.Parts?
            .Select(part => part.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        if (content is null)
        {
            throw new LlmProviderException(
                "Google Gemini returned no model content.",
                providerCode: "empty_response");
        }

        return new LlmProviderResponse(
            content,
            LlmProviderKind.GoogleGemini,
            model,
            response?.Candidates?.FirstOrDefault()?.FinishReason);
    }

    private LlmProviderException CreateProviderException(
        HttpStatusCode statusCode,
        string responseBody)
    {
        string? message = null;
        string? code = null;
        try
        {
            var error = JsonSerializer.Deserialize<GeminiErrorEnvelope>(responseBody, JsonOptions)?.Error;
            message = error?.Message;
            code = error?.Status;
        }
        catch (JsonException)
        {
            // Keep the normalized provider error when Gemini does not return JSON.
        }

        var safeMessage = message ?? "Google Gemini returned an error response.";
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            safeMessage = safeMessage.Replace(_options.ApiKey, "[REDACTED]", StringComparison.Ordinal);
        }

        return new LlmProviderException(
            safeMessage,
            (int)statusCode,
            code);
    }

    private sealed record GeminiRequest(
        IReadOnlyList<GeminiContent> Contents,
        GeminiSystemInstruction? SystemInstruction,
        GeminiGenerationConfig? GenerationConfig);

    private sealed record GeminiContent(string Role, IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart(string Text);

    private sealed record GeminiSystemInstruction(IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("responseMimeType")] string ResponseMimeType);

    private sealed class GeminiResponse
    {
        public List<GeminiCandidate>? Candidates { get; init; }
    }

    private sealed class GeminiCandidate
    {
        public GeminiContent? Content { get; init; }

        public string? FinishReason { get; init; }
    }

    private sealed class GeminiErrorEnvelope
    {
        public GeminiError? Error { get; init; }
    }

    private sealed record GeminiError(string? Message, string? Status);
}
