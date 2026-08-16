using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Contracts;

public sealed record LlmProviderRequest
{
    public LlmProviderRequest(
        IReadOnlyList<LlmMessage> messages,
        LlmProviderKind provider,
        string? model = null,
        TimeSpan? timeout = null,
        double? temperature = null,
        int? maxTokens = null,
        string? outputSchema = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        Messages = messages.ToArray();
        Provider = provider;
        Model = model;
        Timeout = timeout ?? TimeSpan.FromSeconds(30);
        Temperature = temperature;
        MaxTokens = maxTokens;
        OutputSchema = outputSchema;
    }

    public IReadOnlyList<LlmMessage> Messages { get; }

    public LlmProviderKind Provider { get; }

    public string? Model { get; }

    public TimeSpan Timeout { get; }

    public double? Temperature { get; }

    public int? MaxTokens { get; }

    public string? OutputSchema { get; }
}
