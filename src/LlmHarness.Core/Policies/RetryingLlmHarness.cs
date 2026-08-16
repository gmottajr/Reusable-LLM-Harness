using LlmHarness.Core.Contracts;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Core.Policies;

public sealed class RetryingLlmHarness : ILlmHarness
{
    private readonly ILlmHarness _innerHarness;
    private readonly LlmRetryPolicy _retryPolicy;

    public RetryingLlmHarness(
        ILlmHarness innerHarness,
        LlmRetryPolicy retryPolicy)
    {
        _innerHarness = innerHarness ?? throw new ArgumentNullException(nameof(innerHarness));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
    }

    public Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
        LlmRequest request,
        CancellationToken cancellationToken = default) =>
        _retryPolicy.ExecuteAsync(
            token => _innerHarness.ExecuteAsync<TOutput>(request, token),
            cancellationToken);
}
