using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Policies;
using LlmHarness.Core.ProviderSelection;
using LlmHarness.Core.Schema;
using LlmHarness.Core.Validation;

namespace LlmHarness.Core.Harness;

public sealed class LlmHarness : ILlmHarness
{
    private readonly IReadOnlyList<ILlmProvider> _providers;
    private readonly ILlmRequestValidator _requestValidator;
    private readonly ISchemaValidator _schemaValidator;
    private readonly LlmRetryPolicy _retryPolicy;
    private readonly LlmTimeoutOptions _timeoutOptions;
    private readonly ILlmHarnessLogger _logger;
    private readonly LlmProviderKind? _fallbackProviderKind;
    private readonly IProviderSelector _providerSelector;

    public LlmHarness(
        IEnumerable<ILlmProvider> providers,
        ILlmRequestValidator? requestValidator = null,
        ISchemaValidator? schemaValidator = null,
        LlmRetryPolicy? retryPolicy = null,
        LlmTimeoutOptions? timeoutOptions = null,
        ILlmHarnessLogger? logger = null,
        LlmProviderKind? fallbackProviderKind = null,
        ProviderSelectionOptions? providerSelectionOptions = null,
        IProviderSelector? providerSelector = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToArray();
        _requestValidator = requestValidator ?? new LlmRequestValidator();
        _schemaValidator = schemaValidator ?? new JsonSchemaValidator();
        _retryPolicy = retryPolicy ?? new LlmRetryPolicy();
        _timeoutOptions = timeoutOptions ?? new LlmTimeoutOptions();
        _timeoutOptions.Validate();
        _logger = logger ?? new NullLlmHarnessLogger();
        _fallbackProviderKind = fallbackProviderKind;
        _providerSelector = providerSelector ??
            new ProviderSelector(_providers, providerSelectionOptions);
    }

    public async Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var validation = _requestValidator.Validate(request);
        if (!validation.IsValid)
        {
            var validationResult = LlmResult<TOutput>.CreateFailure(
                validation.Errors[0],
                Metadata(correlationId, request));
            LogCompleted(correlationId, request, validationResult);
            return validationResult;
        }

        var selection = await _providerSelector.SelectAsync(request, cancellationToken);
        if (selection.Error is not null || selection.Provider is null)
        {
            var providerResult = LlmResult<TOutput>.CreateFailure(
                selection.Error ?? ProviderUnavailableError(),
                Metadata(correlationId, request));
            LogCompleted(correlationId, request, providerResult);
            return providerResult;
        }

        var fallback = await SelectFallbackProviderAsync(
            selection.Provider,
            cancellationToken);

        LogStarted(correlationId, request, selection.Provider);

        try
        {
            var primary = new RetryingLlmHarness(
                new ProviderCallHarness(selection.Provider),
                _retryPolicy);
            ILlmHarness? fallbackHarness = fallback is null
                ? null
                : new RetryingLlmHarness(
                    new ProviderCallHarness(fallback),
                    _retryPolicy);

            ILlmHarness pipeline = new TimeoutLlmHarness(
                primary,
                _timeoutOptions,
                fallbackHarness,
                fallback?.Kind);
            pipeline = new SchemaValidatingLlmHarness(pipeline, _schemaValidator);

            var result = await pipeline.ExecuteAsync<TOutput>(request, cancellationToken);
            result = WithCorrelationId(result, correlationId);
            LogCompleted(correlationId, request, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = LlmResult<TOutput>.CreateFailure(
                LlmRetryClassifier.FromException(exception),
                Metadata(correlationId, request, selection.Provider.Kind));
            LogCompleted(correlationId, request, failure);
            return failure;
        }
    }

    private async Task<ILlmProvider?> SelectFallbackProviderAsync(
        ILlmProvider primary,
        CancellationToken cancellationToken)
    {
        if (_fallbackProviderKind is null || _fallbackProviderKind == primary.Kind)
        {
            return null;
        }

        var fallback = _providers.FirstOrDefault(
            provider => provider.Kind == _fallbackProviderKind);
        if (fallback is null)
        {
            return null;
        }

        try
        {
            return await fallback.IsAvailableAsync(cancellationToken)
                ? fallback
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void LogStarted(
        string correlationId,
        LlmRequest request,
        ILlmProvider provider) =>
        Log(new LlmLogEvent
        {
            CorrelationId = correlationId,
            Status = "started",
            Provider = provider.Kind,
            Model = request.Model
        });

    private void LogCompleted<TOutput>(
        string correlationId,
        LlmRequest? request,
        LlmResult<TOutput> result) =>
        Log(new LlmLogEvent
        {
            CorrelationId = correlationId,
            Status = result.Success ? "success" : "failure",
            Provider = result.Metadata.SelectedProvider ?? result.Metadata.Provider,
            Model = result.Metadata.Model ?? request?.Model,
            Duration = result.Metadata.Duration,
            Attempts = result.Metadata.AttemptCount,
            RetryCount = result.Metadata.RetryCount,
            Success = result.Success,
            ErrorType = result.Error?.Type,
            FallbackUsed = result.Metadata.FallbackUsed
        });

    private void Log(LlmLogEvent logEvent)
    {
        try
        {
            _logger.Log(logEvent);
        }
        catch
        {
            // Logging must not turn a provider result into a harness failure.
        }
    }

    private static LlmResult<TOutput> WithCorrelationId<TOutput>(
        LlmResult<TOutput> result,
        string correlationId)
    {
        var metadata = result.Metadata with
        {
            CorrelationId = correlationId,
            CompletedAt = result.Metadata.CompletedAt ?? DateTimeOffset.UtcNow
        };

        return result.Success
            ? LlmResult<TOutput>.CreateSuccess(result.Output!, metadata)
            : LlmResult<TOutput>.CreateFailure(result.Error!, metadata);
    }

    private static LlmMetadata Metadata(
        string correlationId,
        LlmRequest? request,
        LlmProviderKind? provider = null) =>
        new()
        {
            CorrelationId = correlationId,
            Provider = provider ?? request?.Provider,
            SelectedProvider = provider ?? request?.Provider,
            Model = request?.Model,
            CompletedAt = DateTimeOffset.UtcNow
        };

    private static LlmError ProviderUnavailableError() =>
        new(
            LlmErrorType.ProviderUnavailable,
            "No provider is available for this request.",
            Retryable: false,
            Code: "provider");
}
