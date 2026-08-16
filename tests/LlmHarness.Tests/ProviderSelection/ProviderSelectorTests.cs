using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.ProviderSelection;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Tests.ProviderSelection;

public sealed class ProviderSelectorTests
{
    [Fact]
    public async Task Manual_selection_uses_the_requested_openai_provider()
    {
        var openAi = new FakeProvider(LlmProviderKind.OpenAI, available: true);
        var local = new FakeProvider(LlmProviderKind.Ollama, available: true);
        var selector = new ProviderSelector([openAi, local]);

        var result = await selector.SelectAsync(
            Request(LlmProviderKind.OpenAI, LlmExecutionMode.Manual));

        Assert.True(result.Success);
        Assert.Same(openAi, result.Provider);
    }

    [Fact]
    public async Task Manual_selection_uses_a_local_provider()
    {
        var openAi = new FakeProvider(LlmProviderKind.OpenAI, available: true);
        var local = new FakeProvider(LlmProviderKind.Ollama, available: true);
        var selector = new ProviderSelector([openAi, local]);

        var result = await selector.SelectAsync(
            Request(LlmProviderKind.Ollama, LlmExecutionMode.Manual));

        Assert.True(result.Success);
        Assert.Same(local, result.Provider);
    }

    [Fact]
    public async Task Auto_prefer_cloud_selects_openai_before_local()
    {
        var openAi = new FakeProvider(LlmProviderKind.OpenAI, available: true);
        var local = new FakeProvider(LlmProviderKind.Ollama, available: true);
        var selector = new ProviderSelector([local, openAi]);

        var result = await selector.SelectAsync(
            Request(provider: null, LlmExecutionMode.AutoPreferCloud));

        Assert.True(result.Success);
        Assert.Same(openAi, result.Provider);
    }

    [Fact]
    public async Task Auto_prefer_local_selects_ollama_before_cloud()
    {
        var openAi = new FakeProvider(LlmProviderKind.OpenAI, available: true);
        var local = new FakeProvider(LlmProviderKind.Ollama, available: true);
        var selector = new ProviderSelector([openAi, local]);

        var result = await selector.SelectAsync(
            Request(provider: null, LlmExecutionMode.AutoPreferLocal));

        Assert.True(result.Success);
        Assert.Same(local, result.Provider);
    }

    [Fact]
    public async Task Unavailable_providers_return_a_non_retryable_structured_error()
    {
        var selector = new ProviderSelector(
            [new FakeProvider(LlmProviderKind.OpenAI, available: false)]);

        var result = await selector.SelectAsync(
            Request(LlmProviderKind.OpenAI, LlmExecutionMode.Manual));

        Assert.False(result.Success);
        Assert.Null(result.Provider);
        Assert.Equal(LlmErrorType.ProviderUnavailable, result.Error!.Type);
        Assert.Equal("No configured LLM provider is available.", result.Error.Message);
        Assert.False(result.Error.Retryable);
    }

    [Fact]
    public async Task Default_selection_mode_is_auto_prefer_cloud()
    {
        var openAi = new FakeProvider(LlmProviderKind.OpenAI, available: true);
        var local = new FakeProvider(LlmProviderKind.Ollama, available: true);
        var selector = new ProviderSelector([local, openAi]);

        var result = await selector.SelectAsync(Request(provider: null));

        Assert.True(result.Success);
        Assert.Same(openAi, result.Provider);
    }

    private static LlmRequest Request(
        LlmProviderKind? provider,
        LlmExecutionMode? executionMode = null) =>
        new(
            [new(LlmMessageRole.User, "Hello")],
            model: "demo-model",
            provider: provider,
            executionMode: executionMode);

    private sealed class FakeProvider(
        LlmProviderKind kind,
        bool available) : ILlmProvider
    {
        public LlmProviderKind Kind { get; } = kind;

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(available);

        public Task<LlmProviderResponse> CompleteAsync(
            LlmProviderRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<LlmProviderResponse>(
                new NotSupportedException("Provider calls are not part of selector tests."));
    }
}
