using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Policies;

public sealed class LlmRetryPolicy
{
    private readonly LlmRetryOptions _options;
    private readonly Random _random;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public LlmRetryPolicy(
        LlmRetryOptions? options = null,
        Random? random = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _options = options ?? new LlmRetryOptions();
        _options.Validate();
        _random = random ?? Random.Shared;
        _delayAsync = delayAsync ?? ((delay, cancellationToken) =>
            Task.Delay(delay, cancellationToken));
    }

    public async Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
        Func<CancellationToken, Task<LlmResult<TOutput>>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            LlmResult<TOutput> result;
            try
            {
                result = await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var error = LlmRetryClassifier.FromException(new TimeoutException());
                if (!ShouldRetry(error, attempt))
                {
                    return Failure<TOutput>(error, attempt);
                }

                await DelayBeforeRetry(attempt, cancellationToken);
                continue;
            }
            catch (Exception exception)
            {
                var error = LlmRetryClassifier.FromException(exception);
                if (!ShouldRetry(error, attempt))
                {
                    return Failure<TOutput>(error, attempt);
                }

                await DelayBeforeRetry(attempt, cancellationToken);
                continue;
            }

            var normalizedResult = WithAttemptMetadata(result, attempt);
            if (!ShouldRetry(result.Error, attempt))
            {
                return normalizedResult;
            }

            await DelayBeforeRetry(attempt, cancellationToken);
        }
    }

    private bool ShouldRetry(LlmError? error, int attempt) =>
        LlmRetryClassifier.IsRetryable(error) && attempt <= _options.MaxRetries;

    private async Task DelayBeforeRetry(int attempt, CancellationToken cancellationToken)
    {
        var delay = CalculateDelay(attempt);
        await _delayAsync(delay, cancellationToken);
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        var initialMilliseconds = _options.InitialDelay.TotalMilliseconds;
        var maximumMilliseconds = _options.MaxDelay.TotalMilliseconds;
        var exponentialMilliseconds = Math.Min(
            maximumMilliseconds,
            initialMilliseconds * Math.Pow(2, attempt - 1));

        if (!_options.UseJitter || exponentialMilliseconds == 0)
        {
            return TimeSpan.FromMilliseconds(exponentialMilliseconds);
        }

        var jitterMilliseconds = _random.NextDouble() * exponentialMilliseconds * 0.25;
        return TimeSpan.FromMilliseconds(
            Math.Min(maximumMilliseconds, exponentialMilliseconds + jitterMilliseconds));
    }

    private static LlmResult<TOutput> WithAttemptMetadata<TOutput>(
        LlmResult<TOutput> result,
        int attempt)
    {
        var metadata = result.Metadata with
        {
            AttemptCount = attempt,
            RetryCount = attempt - 1
        };

        return result.Success
            ? LlmResult<TOutput>.CreateSuccess(result.Output!, metadata)
            : LlmResult<TOutput>.CreateFailure(
                result.Error ?? new LlmError(
                    LlmErrorType.UnknownError,
                    "The LLM provider returned an invalid failure result.",
                    Retryable: false),
                metadata);
    }

    private static LlmResult<TOutput> Failure<TOutput>(LlmError error, int attempt) =>
        LlmResult<TOutput>.CreateFailure(
            error,
            new LlmMetadata
            {
                AttemptCount = attempt,
                RetryCount = attempt - 1
            });
}
