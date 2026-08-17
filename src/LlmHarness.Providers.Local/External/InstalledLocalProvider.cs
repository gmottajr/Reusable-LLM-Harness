using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Providers.Local.External;

public sealed class InstalledLocalProvider : ILlmProvider, IProviderAvailabilityDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly InstalledLocalProviderOptions _options;

    public InstalledLocalProvider(
        HttpClient httpClient,
        InstalledLocalProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public LlmProviderKind Kind => LlmProviderKind.Ollama;

    public string? AvailabilityReason { get; private set; }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.TryValidate(out var configurationError))
        {
            AvailabilityReason = configurationError;
            return false;
        }

        try
        {
            using var request = CreateRequest(HttpMethod.Get, ModelsUri());
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AvailabilityReason = $"Installed local server returned HTTP {(int)response.StatusCode}.";
                return false;
            }

            AvailabilityReason = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            AvailabilityReason = "Could not connect to the installed local LLM server.";
            return false;
        }
        catch (InvalidOperationException)
        {
            AvailabilityReason = "The installed local endpoint is invalid.";
            return false;
        }
    }

    public async Task<LlmProviderResponse> CompleteAsync(
        LlmProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _options.Validate();

        var model = request.Model ?? _options.Model;
        using var httpRequest = CreateRequest(HttpMethod.Post, CompletionUri());
        httpRequest.Content = JsonContent.Create(
            new
            {
                model,
                messages = request.Messages.Select(message => new
                {
                    role = RoleName(message.Role),
                    content = message.Content
                }),
                temperature = request.Temperature,
                max_tokens = request.MaxTokens
            },
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmProviderException(
                "The installed local LLM server returned an error.",
                (int)response.StatusCode,
                providerCode: TryGetProviderCode(body));
        }

        return ParseResponse(body, request, model);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        return request;
    }

    private Uri CompletionUri() =>
        new($"{_options.Endpoint.TrimEnd('/')}/chat/completions");

    private Uri ModelsUri() =>
        new($"{_options.Endpoint.TrimEnd('/')}/models");

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
        string body,
        LlmProviderRequest request,
        string model)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions);
            var choice = response?.Choices?.FirstOrDefault();
            if (choice?.Message?.Content is null)
            {
                throw new LlmProviderException(
                    "The installed local LLM server returned no assistant content.",
                    providerCode: "empty_response");
            }

            return new LlmProviderResponse(
                choice.Message.Content,
                LlmProviderKind.Ollama,
                request.Model ?? model,
                choice.FinishReason,
                response?.Id,
                response?.Usage?.PromptTokens,
                response?.Usage?.CompletionTokens);
        }
        catch (JsonException exception)
        {
            throw new LlmProviderException(
                "The installed local LLM server returned an invalid response.",
                providerCode: "invalid_response",
                innerException: exception);
        }
    }

    private static string? TryGetProviderCode(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorEnvelope>(body, JsonOptions)?.Error?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ChatResponse
    {
        public string? Id { get; init; }

        public List<Choice>? Choices { get; init; }

        public Usage? Usage { get; init; }
    }

    private sealed class Choice
    {
        public MessageContent? Message { get; init; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; init; }
    }

    private sealed class MessageContent
    {
        public string? Content { get; init; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; init; }
    }

    private sealed class ErrorEnvelope
    {
        public ErrorDetails? Error { get; init; }
    }

    private sealed class ErrorDetails
    {
        public string? Code { get; init; }
    }
}
