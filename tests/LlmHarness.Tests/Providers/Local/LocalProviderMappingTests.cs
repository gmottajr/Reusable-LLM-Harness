using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Harness;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Policies;

namespace LlmHarness.Tests.Providers.Local;

public sealed class LocalProviderMappingTests
{
    [Fact]
    public async Task Local_provider_kind_response_is_mapped_to_typed_output()
    {
        var provider = new TestProvider();
        var harness = new LlmHarness(
            [provider],
            retryPolicy: new LlmRetryPolicy(
                new LlmRetryOptions { UseJitter = false },
                delayAsync: (_, _) => Task.CompletedTask),
            timeoutOptions: new LlmTimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromSeconds(1)
            });

        var result = await harness.ExecuteAsync<Dictionary<string, string>>(
            new LlmRequest(
                [new(LlmMessageRole.User, "Return local JSON.")],
                model: "local-test-model",
                provider: LlmProviderKind.Ollama,
                executionMode: LlmExecutionMode.Manual));

        Assert.True(result.Success);
        Assert.Equal("local", result.Output!["answer"]);
        Assert.Equal(LlmProviderKind.Ollama, result.Metadata.SelectedProvider);
        Assert.Equal(1, provider.CompleteCalls);
    }

    private sealed class TestProvider : ILlmProvider
    {
        public LlmProviderKind Kind => LlmProviderKind.Ollama;

        public int CompleteCalls { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<LlmProviderResponse> CompleteAsync(
            LlmProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return Task.FromResult(
                new LlmProviderResponse(
                    "{\"answer\":\"local\"}",
                    LlmProviderKind.Ollama,
                    request.Model));
        }
    }
}
