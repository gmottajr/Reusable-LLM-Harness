using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Core.Interfaces;
using LlmHarness.ManagedModels.Catalog;
using LlmHarness.ManagedModels.Models;
using LlmHarness.ManagedModels.Runtime;
using LlmHarness.ManagedModels.Storage;

namespace LlmHarness.Providers.Local.Managed;

public sealed class ManagedLocalProvider : ILlmProvider, IProviderAvailabilityDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IModelCatalogService _catalog;
    private readonly IModelStorageService _storage;
    private readonly IModelRuntimeService _runtime;
    private readonly ManagedLocalProviderOptions _options;

    public ManagedLocalProvider(
        HttpClient httpClient,
        IModelCatalogService catalog,
        IModelStorageService storage,
        IModelRuntimeService runtime,
        ManagedLocalProviderOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _options = options ?? new ManagedLocalProviderOptions();
    }

    public LlmProviderKind Kind => LlmProviderKind.LocalOpenAiCompatible;

    public string? AvailabilityReason { get; private set; }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var model = _catalog.Find(_options.DefaultModelId);
        if (model is null)
        {
            AvailabilityReason = "The configured managed model is not in the curated catalog.";
            return false;
        }

        var stored = await _storage.InspectAsync(model, cancellationToken);
        if (!stored.IsValid)
        {
            AvailabilityReason = "The managed model has not been downloaded and verified.";
            return false;
        }

        var runtime = await _runtime.GetStatusAsync(cancellationToken);
        var runtimeHasModel = runtime.State == ManagedRuntimeState.Running &&
            string.Equals(runtime.ModelId, model.Id, StringComparison.OrdinalIgnoreCase);
        if (!runtimeHasModel && !_options.AutoStart)
        {
            AvailabilityReason = "The managed local runtime is not running this model.";
            return false;
        }

        AvailabilityReason = null;
        return true;
    }

    public async Task<LlmProviderResponse> CompleteAsync(
        LlmProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var model = _catalog.Find(request.Model ?? _options.DefaultModelId);
        if (model is null)
        {
            throw new LlmProviderException(
                "The requested managed model is not in the curated catalog.",
                statusCode: 400,
                providerCode: "unknown_model");
        }

        var stored = await _storage.InspectAsync(model, cancellationToken);
        if (!stored.IsValid)
        {
            throw new LlmProviderException(
                "The requested managed model is not downloaded and verified.",
                statusCode: 503,
                providerCode: "model_not_downloaded");
        }

        var runtime = await _runtime.GetStatusAsync(cancellationToken);
        if (runtime.State != ManagedRuntimeState.Running ||
            !string.Equals(runtime.ModelId, model.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (!_options.AutoStart)
            {
                throw new LlmProviderException(
                    "The managed local runtime is not running the requested model.",
                    statusCode: 503,
                    providerCode: "runtime_not_running");
            }

            runtime = await _runtime.StartAsync(
                model,
                _storage.GetModelPath(model),
                cancellationToken);
            if (runtime.State != ManagedRuntimeState.Running)
            {
                throw new LlmProviderException(
                    runtime.Error ?? "The managed local runtime could not be started.",
                    statusCode: 503,
                    providerCode: "runtime_start_failed");
            }
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _runtime.GetCompletionUri())
        {
            Content = JsonContent.Create(
                new
                {
                    model = model.RuntimeModelName,
                    messages = request.Messages.Select(message => new
                    {
                        role = RoleName(message.Role),
                        content = message.Content
                    }),
                    temperature = request.Temperature,
                    max_tokens = request.MaxTokens
                },
                options: JsonOptions)
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmProviderException(
                "The managed local runtime returned an error.",
                (int)response.StatusCode,
                TryGetProviderCode(body));
        }

        return ParseResponse(body, request, model);
    }

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
        ManagedModelDefinition model)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions);
            var choice = response?.Choices?.FirstOrDefault();
            if (choice?.Message?.Content is null)
            {
                throw new LlmProviderException(
                    "The managed local runtime returned no assistant content.",
                    providerCode: "empty_response");
            }

            return new LlmProviderResponse(
                choice.Message.Content,
                LlmProviderKind.LocalOpenAiCompatible,
                request.Model ?? model.Id,
                choice.FinishReason,
                response?.Id,
                response?.Usage?.PromptTokens,
                response?.Usage?.CompletionTokens);
        }
        catch (JsonException exception)
        {
            throw new LlmProviderException(
                "The managed local runtime returned an invalid response.",
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
