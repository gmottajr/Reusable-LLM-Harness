using LlmHarness.Core.Harness;

namespace LlmHarness.Api.Logging;

public sealed class AspNetLlmHarnessLogger(
    ILogger<AspNetLlmHarnessLogger> logger) : ILlmHarnessLogger
{
    public void Log(LlmLogEvent logEvent) =>
        logger.LogInformation(
            logEvent.RawResponse is null
                ? "HarnessEvent status={Status} provider={Provider} model={Model} attempts={Attempts} retryCount={RetryCount} durationMs={DurationMs} success={Success} errorType={ErrorType} errorCode={ErrorCode} errorMessage={ErrorMessage} fallbackUsed={FallbackUsed} correlationId={CorrelationId}"
                : "HarnessEvent status={Status} provider={Provider} model={Model} attempts={Attempts} retryCount={RetryCount} durationMs={DurationMs} success={Success} errorType={ErrorType} errorCode={ErrorCode} errorMessage={ErrorMessage} fallbackUsed={FallbackUsed} correlationId={CorrelationId} validationPath={ValidationPath} rawResponse={RawResponse} normalizedResponse={NormalizedResponse} outputSchema={OutputSchema}",
            logEvent.Status,
            logEvent.Provider,
            logEvent.Model,
            logEvent.Attempts,
            logEvent.RetryCount,
            logEvent.Duration?.TotalMilliseconds,
            logEvent.Success,
            logEvent.ErrorType,
            logEvent.ErrorCode,
            logEvent.ErrorMessage,
            logEvent.FallbackUsed,
            logEvent.CorrelationId,
            logEvent.ValidationPath,
            logEvent.RawResponse,
            logEvent.NormalizedResponse,
            logEvent.OutputSchema);
}
