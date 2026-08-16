using LlmHarness.Core.Contracts;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Core.ProviderSelection;

public sealed record ProviderSelectionResult(
    ILlmProvider? Provider,
    LlmError? Error)
{
    public bool Success => Provider is not null && Error is null;

    public static ProviderSelectionResult Found(ILlmProvider provider) =>
        new(provider, null);

    public static ProviderSelectionResult Failed(LlmError error) =>
        new(null, error);
}
