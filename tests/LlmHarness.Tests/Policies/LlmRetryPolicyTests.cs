using System.Net.Http;
using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Policies;

namespace LlmHarness.Tests.Policies;

public sealed class LlmRetryPolicyTests
{
    [Fact]
    public async Task Rate_limit_error_is_retried_with_exponential_backoff()
    {
        var delays = new List<TimeSpan>();
        var inner = new ScriptedHarness(
            () => throw new LlmProviderException("rate limited", statusCode: 429),
            () => throw new LlmProviderException("rate limited", statusCode: 429),
            Success);
        var harness = CreateHarness(inner, delays);

        var result = await harness.ExecuteAsync<string>(ValidRequest());

        Assert.True(result.Success);
        Assert.Equal(3, inner.Calls);
        Assert.Equal(3, result.Metadata.AttemptCount);
        Assert.Equal(2, result.Metadata.RetryCount);
        Assert.Equal(
            new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1) },
            delays);
    }

    [Fact]
    public async Task Server_error_is_retried()
    {
        var inner = new ScriptedHarness(
            () => throw new LlmProviderException("server error", statusCode: 500),
            Success);
        var harness = CreateHarness(inner, []);

        var result = await harness.ExecuteAsync<string>(ValidRequest());

        Assert.True(result.Success);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(2, result.Metadata.AttemptCount);
        Assert.Equal(1, result.Metadata.RetryCount);
    }

    [Fact]
    public async Task Transient_network_error_is_retried()
    {
        var inner = new ScriptedHarness(
            () => throw new HttpRequestException("connection reset"),
            Success);
        var harness = CreateHarness(inner, []);

        var result = await harness.ExecuteAsync<string>(ValidRequest());

        Assert.True(result.Success);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Http_client_error_is_not_retried()
    {
        var inner = new ScriptedHarness(
            () => throw new HttpRequestException(
                "bad request",
                inner: null,
                statusCode: System.Net.HttpStatusCode.BadRequest));
        var harness = CreateHarness(inner, []);

        var result = await harness.ExecuteAsync<string>(ValidRequest());

        Assert.False(result.Success);
        Assert.Equal(1, inner.Calls);
        Assert.False(result.Error!.Retryable);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    public async Task Client_errors_are_not_retried(int statusCode)
    {
        var inner = new ScriptedHarness(
            () => throw new LlmProviderException("request rejected", statusCode));
        var harness = CreateHarness(inner, []);

        var result = await harness.ExecuteAsync<string>(ValidRequest());

        Assert.False(result.Success);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(LlmErrorType.ProviderError, result.Error!.Type);
        Assert.False(result.Error.Retryable);
        Assert.Equal(1, result.Metadata.AttemptCount);
        Assert.Equal(0, result.Metadata.RetryCount);
    }

    [Fact]
    public async Task Retry_limit_is_enforced_and_final_error_is_structured()
    {
        var inner = new ScriptedHarness(
            () => throw new LlmProviderException("unavailable", statusCode: 503),
            () => throw new LlmProviderException("unavailable", statusCode: 503),
            () => throw new LlmProviderException("unavailable", statusCode: 503),
            () => throw new LlmProviderException("unavailable", statusCode: 503));
        var harness = CreateHarness(inner, [], maxRetries: 2);

        var result = await harness.ExecuteAsync<string>(ValidRequest());

        Assert.False(result.Success);
        Assert.Equal(3, inner.Calls);
        Assert.Equal(LlmErrorType.ProviderError, result.Error!.Type);
        Assert.True(result.Error.Retryable);
        Assert.Equal(3, result.Metadata.AttemptCount);
        Assert.Equal(2, result.Metadata.RetryCount);
        Assert.NotNull(result.Error.Message);
    }

    [Fact]
    public async Task Non_retryable_validation_result_is_not_retried()
    {
        var inner = new ScriptedHarness(
            () => LlmResult<string>.CreateFailure(
                new LlmError(
                    LlmErrorType.InputValidationError,
                    "Invalid input.",
                    Retryable: false)));
        var harness = CreateHarness(inner, []);

        var result = await harness.ExecuteAsync<string>(ValidRequest());

        Assert.False(result.Success);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(LlmErrorType.InputValidationError, result.Error!.Type);
        Assert.Equal(1, result.Metadata.AttemptCount);
        Assert.Equal(0, result.Metadata.RetryCount);
    }

    private static RetryingLlmHarness CreateHarness(
        ScriptedHarness inner,
        List<TimeSpan> delays,
        int maxRetries = 3) =>
        new(
            inner,
            new LlmRetryPolicy(
                new LlmRetryOptions
                {
                    MaxRetries = maxRetries,
                    InitialDelay = TimeSpan.FromMilliseconds(500),
                    MaxDelay = TimeSpan.FromSeconds(5),
                    UseJitter = false
                },
                delayAsync: (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }));

    private static LlmRequest ValidRequest() =>
        new(
            [new(LlmMessageRole.User, "Hello")],
            model: "demo-model",
            provider: LlmProviderKind.OpenAI);

    private static LlmResult<string> Success() =>
        LlmResult<string>.CreateSuccess("done");

    private sealed class ScriptedHarness(
        params Func<LlmResult<string>>[] steps) : ILlmHarness
    {
        private readonly Queue<Func<LlmResult<string>>> _steps = new(steps);

        public int Calls { get; private set; }

        public Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
            LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var result = _steps.Dequeue()();

            if (!result.Success)
            {
                return Task.FromResult(
                    LlmResult<TOutput>.CreateFailure(result.Error!, result.Metadata));
            }

            return Task.FromResult(
                LlmResult<TOutput>.CreateSuccess(
                    (TOutput)(object)result.Output!,
                    result.Metadata));
        }
    }
}
