using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Contracts;

public sealed record LlmProviderResponse
{
    public LlmProviderResponse(
        string? content,
        LlmProviderKind provider,
        string? model = null,
        string? finishReason = null,
        string? providerRequestId = null,
        int? promptTokens = null,
        int? completionTokens = null)
    {
        Content = content;
        Provider = provider;
        Model = model;
        FinishReason = finishReason;
        ProviderRequestId = providerRequestId;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
    }

    public string? Content { get; }

    public LlmProviderKind Provider { get; }

    public string? Model { get; }

    public string? FinishReason { get; }

    public string? ProviderRequestId { get; }

    public int? PromptTokens { get; }

    public int? CompletionTokens { get; }
}
