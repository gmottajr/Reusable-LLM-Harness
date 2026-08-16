using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Configuration;

public sealed record ProviderSelectionOptions
{
    public LlmExecutionMode DefaultExecutionMode { get; init; } = LlmExecutionMode.AutoPreferCloud;

    public IReadOnlyList<LlmProviderKind> CloudPreference { get; init; } =
    [
        LlmProviderKind.OpenAI,
        LlmProviderKind.Anthropic,
        LlmProviderKind.Ollama,
        LlmProviderKind.LocalOpenAiCompatible
    ];

    public IReadOnlyList<LlmProviderKind> LocalPreference { get; init; } =
    [
        LlmProviderKind.Ollama,
        LlmProviderKind.LocalOpenAiCompatible,
        LlmProviderKind.OpenAI,
        LlmProviderKind.Anthropic
    ];

    public void Validate()
    {
        if (!Enum.IsDefined(DefaultExecutionMode) ||
            DefaultExecutionMode == LlmExecutionMode.Manual)
        {
            throw new ArgumentException(
                "Default provider selection mode must be an automatic mode.",
                nameof(DefaultExecutionMode));
        }

        ValidatePreference(CloudPreference, nameof(CloudPreference));
        ValidatePreference(LocalPreference, nameof(LocalPreference));
    }

    private static void ValidatePreference(
        IReadOnlyList<LlmProviderKind> preference,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(preference);
        if (preference.Count == 0 || preference.Any(kind => !Enum.IsDefined(kind)))
        {
            throw new ArgumentException(
                "Provider preference must contain at least one valid provider kind.",
                parameterName);
        }
    }
}
