using LlmHarness.Core.Contracts;

namespace LlmHarness.Core.Interfaces;

public interface ILlmHarness
{
    Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
        LlmRequest request,
        CancellationToken cancellationToken = default);
}
