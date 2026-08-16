using LlmHarness.Core.Contracts;

namespace LlmHarness.Core.ProviderSelection;

public interface IProviderSelector
{
    Task<ProviderSelectionResult> SelectAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default);
}
