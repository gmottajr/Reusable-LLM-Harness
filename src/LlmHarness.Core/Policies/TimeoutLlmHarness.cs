using System.Diagnostics;
using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Core.Policies;

public sealed class TimeoutLlmHarness : ILlmHarness
{
    private readonly ILlmHarness _primaryHarness;
    private readonly ILlmHarness? _fallbackHarness;
    private readonly LlmTimeoutOptions _options;
    private readonly LlmProviderKind? _fallbackProvider;

    public TimeoutLlmHarness(
        ILlmHarness primaryHarness,
        LlmTimeoutOptions? options = null,
        ILlmHarness? fallbackHarness = null,
        LlmProviderKind? fallbackProvider = null)
    {
        _primaryHarness = primaryHarness ?? throw new ArgumentNullException(nameof(primaryHarness));
        _fallbackHarness = fallbackHarness;
        _options = options ?? new LlmTimeoutOptions();
        _options.Validate();
        _fallbackProvider = fallbackProvider;
    }

    public async Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeout = ResolveTimeout(request);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var primaryResult = await ExecuteWithTimeout(
                _primaryHarness,
                request,
                timeout,
                cancellationToken);

            if (IsTimeout(primaryResult.Error))
            {
                return await ExecuteFallbackOrReturnTimeout(
                    request,
                    timeout,
                    stopwatch,
                    cancellationToken,
                    primaryResult);
            }

            return WithMetadata(
                primaryResult,
                request,
                stopwatch.Elapsed,
                timeout,
                fallbackUsed: false,
                selectedProvider: request.Provider);
        }
        catch (TimeoutException)
        {
            return await ExecuteFallbackOrReturnTimeout<TOutput>(
                request,
                timeout,
                stopwatch,
                cancellationToken,
                timeoutResult: null);
        }
    }

    private async Task<LlmResult<TOutput>> ExecuteFallbackOrReturnTimeout<TOutput>(
        LlmRequest request,
        TimeSpan timeout,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        LlmResult<TOutput>? timeoutResult)
    {
        if (_fallbackHarness is null)
        {
            var error = timeoutResult?.Error ?? TimeoutError();
            return LlmResult<TOutput>.CreateFailure(
                error,
                Metadata(
                    request,
                    stopwatch.Elapsed,
                    timeout,
                    fallbackUsed: false,
                    selectedProvider: request.Provider));
        }

        try
        {
            var fallbackResult = await ExecuteWithTimeout(
                _fallbackHarness,
                request,
                timeout,
                cancellationToken);

            if (IsTimeout(fallbackResult.Error))
            {
                return LlmResult<TOutput>.CreateFailure(
                    TimeoutError(),
                    Metadata(
                        request,
                        stopwatch.Elapsed,
                        timeout,
                        fallbackUsed: true,
                        selectedProvider: _fallbackProvider ?? request.Provider));
            }

            return WithMetadata(
                fallbackResult,
                request,
                stopwatch.Elapsed,
                timeout,
                fallbackUsed: true,
                selectedProvider: _fallbackProvider ?? request.Provider);
        }
        catch (TimeoutException)
        {
            return LlmResult<TOutput>.CreateFailure(
                TimeoutError(),
                Metadata(
                    request,
                    stopwatch.Elapsed,
                    timeout,
                    fallbackUsed: true,
                    selectedProvider: _fallbackProvider ?? request.Provider));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LlmResult<TOutput>.CreateFailure(
                LlmRetryClassifier.FromException(exception),
                Metadata(
                    request,
                    stopwatch.Elapsed,
                    timeout,
                    fallbackUsed: true,
                    selectedProvider: _fallbackProvider ?? request.Provider));
        }
    }

    private static async Task<LlmResult<TOutput>> ExecuteWithTimeout<TOutput>(
        ILlmHarness harness,
        LlmRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await harness.ExecuteAsync<TOutput>(request, timeoutSource.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException("The LLM request timed out.", exception);
        }
    }

    private TimeSpan ResolveTimeout(LlmRequest request) =>
        request.HasTimeoutOverride
            ? request.Timeout
            : _options.DefaultTimeout;

    private static bool IsTimeout(LlmError? error) =>
        error?.Type == LlmErrorType.TimeoutError;

    private static LlmError TimeoutError() =>
        new(
            LlmErrorType.TimeoutError,
            "LLM request timed out.",
            Retryable: true);

    private static LlmResult<TOutput> WithMetadata<TOutput>(
        LlmResult<TOutput> result,
        LlmRequest request,
        TimeSpan duration,
        TimeSpan timeout,
        bool fallbackUsed,
        LlmProviderKind? selectedProvider)
    {
        var metadata = Metadata(
            request,
            duration,
            timeout,
            fallbackUsed,
            selectedProvider,
            result.Metadata);

        return result.Success
            ? LlmResult<TOutput>.CreateSuccess(result.Output!, metadata)
            : LlmResult<TOutput>.CreateFailure(
                result.Error ?? new LlmError(
                    LlmErrorType.UnknownError,
                    "The LLM provider returned an invalid failure result.",
                    Retryable: false),
                metadata);
    }

    private static LlmMetadata Metadata(
        LlmRequest request,
        TimeSpan duration,
        TimeSpan timeout,
        bool fallbackUsed,
        LlmProviderKind? selectedProvider,
        LlmMetadata? existing = null) =>
        (existing ?? new LlmMetadata()) with
        {
            Provider = selectedProvider ?? existing?.Provider ?? request.Provider,
            SelectedProvider = selectedProvider ?? existing?.SelectedProvider ?? request.Provider,
            Duration = duration,
            TimeoutMs = (long)Math.Ceiling(timeout.TotalMilliseconds),
            FallbackUsed = fallbackUsed || existing?.FallbackUsed == true,
            CompletedAt = DateTimeOffset.UtcNow
        };
}
