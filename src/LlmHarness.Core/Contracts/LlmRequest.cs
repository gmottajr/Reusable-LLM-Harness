using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Contracts;

public sealed record LlmRequest
{
    public LlmRequest(
        IReadOnlyList<LlmMessage> messages,
        string? model = null,
        LlmProviderKind? provider = null,
        LlmExecutionMode? executionMode = null,
        TimeSpan? timeout = null,
        double? temperature = null,
        int? maxTokens = null,
        string? outputSchema = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        Messages = messages.ToArray();
        Model = model;
        Provider = provider;
        HasExecutionModeOverride = executionMode.HasValue;
        ExecutionMode = executionMode ?? LlmExecutionMode.Manual;
        HasTimeoutOverride = timeout.HasValue;
        Timeout = timeout ?? TimeSpan.FromSeconds(30);
        Temperature = temperature;
        MaxTokens = maxTokens;
        OutputSchema = outputSchema;
    }

    public IReadOnlyList<LlmMessage> Messages { get; }

    public string? Model { get; }

    public LlmProviderKind? Provider { get; }

    public LlmExecutionMode ExecutionMode { get; }

    public bool HasExecutionModeOverride { get; }

    public TimeSpan Timeout { get; }

    public bool HasTimeoutOverride { get; }

    public double? Temperature { get; }

    public int? MaxTokens { get; }

    public string? OutputSchema { get; }
}
