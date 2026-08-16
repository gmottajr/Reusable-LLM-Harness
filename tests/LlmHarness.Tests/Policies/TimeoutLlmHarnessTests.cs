using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Policies;

namespace LlmHarness.Tests.Policies;

public sealed class TimeoutLlmHarnessTests
{
    [Fact]
    public async Task Request_that_completes_before_timeout_returns_success()
    {
        var harness = new TimeoutLlmHarness(
            new FixedHarness(Success),
            new LlmTimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(1) });

        var result = await harness.ExecuteAsync<string>(Request());

        Assert.True(result.Success);
        Assert.Equal("done", result.Output);
        Assert.Equal(1000, result.Metadata.TimeoutMs);
        Assert.False(result.Metadata.FallbackUsed);
        Assert.NotNull(result.Metadata.Duration);
    }

    [Fact]
    public async Task Slow_request_returns_structured_timeout_failure()
    {
        var harness = new TimeoutLlmHarness(
            new DelayingHarness(TimeSpan.FromMilliseconds(100)),
            new LlmTimeoutOptions { DefaultTimeout = TimeSpan.FromMilliseconds(10) });

        var result = await harness.ExecuteAsync<string>(Request());

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.TimeoutError, result.Error!.Type);
        Assert.True(result.Error.Retryable);
        Assert.Equal(10, result.Metadata.TimeoutMs);
        Assert.False(result.Metadata.FallbackUsed);
    }

    [Fact]
    public async Task Per_request_timeout_overrides_configured_default()
    {
        var harness = new TimeoutLlmHarness(
            new DelayingHarness(TimeSpan.FromMilliseconds(50)),
            new LlmTimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(1) });

        var result = await harness.ExecuteAsync<string>(
            Request(timeout: TimeSpan.FromMilliseconds(10)));

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.TimeoutError, result.Error!.Type);
        Assert.Equal(10, result.Metadata.TimeoutMs);
    }

    [Fact]
    public async Task Configured_fallback_is_used_once_after_timeout()
    {
        var fallback = new FixedHarness(Success);
        var harness = new TimeoutLlmHarness(
            new DelayingHarness(TimeSpan.FromMilliseconds(100)),
            new LlmTimeoutOptions { DefaultTimeout = TimeSpan.FromMilliseconds(10) },
            fallback,
            LlmProviderKind.Ollama);

        var result = await harness.ExecuteAsync<string>(Request());

        Assert.True(result.Success);
        Assert.Equal("done", result.Output);
        Assert.Equal(1, fallback.Calls);
        Assert.True(result.Metadata.FallbackUsed);
        Assert.Equal(LlmProviderKind.Ollama, result.Metadata.SelectedProvider);
    }

    [Fact]
    public async Task Fallback_failure_returns_structured_error()
    {
        var fallback = new ThrowingHarness(
            new LlmProviderException("fallback unavailable", statusCode: 503));
        var harness = new TimeoutLlmHarness(
            new DelayingHarness(TimeSpan.FromMilliseconds(100)),
            new LlmTimeoutOptions { DefaultTimeout = TimeSpan.FromMilliseconds(10) },
            fallback,
            LlmProviderKind.Ollama);

        var result = await harness.ExecuteAsync<string>(Request());

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.ProviderError, result.Error!.Type);
        Assert.True(result.Error.Retryable);
        Assert.True(result.Metadata.FallbackUsed);
        Assert.Equal(LlmProviderKind.Ollama, result.Metadata.SelectedProvider);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var harness = new TimeoutLlmHarness(
            new DelayingHarness(TimeSpan.FromMilliseconds(100)),
            new LlmTimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(1) });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.ExecuteAsync<string>(Request(), cancellation.Token));
    }

    private static LlmRequest Request(TimeSpan? timeout = null) =>
        new(
            [new(LlmMessageRole.User, "Hello")],
            model: "demo-model",
            provider: LlmProviderKind.OpenAI,
            timeout: timeout);

    private static LlmResult<string> Success() =>
        LlmResult<string>.CreateSuccess("done");

    private sealed class FixedHarness(Func<LlmResult<string>> result) : ILlmHarness
    {
        public int Calls { get; private set; }

        public Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
            LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var value = result();
            return Task.FromResult(
                LlmResult<TOutput>.CreateSuccess((TOutput)(object)value.Output!));
        }
    }

    private sealed class DelayingHarness(TimeSpan delay) : ILlmHarness
    {
        public async Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
            LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return LlmResult<TOutput>.CreateSuccess(default!);
        }
    }

    private sealed class ThrowingHarness(Exception exception) : ILlmHarness
    {
        public Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
            LlmRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<LlmResult<TOutput>>(exception);
    }
}
