using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Interfaces;

public interface ILlmProvider
{
    LlmProviderKind Kind { get; }

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<LlmProviderResponse> CompleteAsync(
        LlmProviderRequest request,
        CancellationToken cancellationToken = default);
}
