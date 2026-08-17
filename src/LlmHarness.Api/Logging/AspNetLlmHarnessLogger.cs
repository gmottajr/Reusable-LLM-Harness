using LlmHarness.Core.Harness;

namespace LlmHarness.Api.Logging;

public sealed class AspNetLlmHarnessLogger(
    ILogger<AspNetLlmHarnessLogger> logger,
    LlmLoggingOptions options) : ILlmHarnessLogger
{
    public void Log(LlmLogEvent logEvent)
    {
        if (!options.IncludePayloads)
        {
            logger.LogInformation(
                "HarnessEvent status={Status} provider={Provider} model={Model} attempts={Attempts} retryCount={RetryCount} durationMs={DurationMs} success={Success} errorType={ErrorType} errorCode={ErrorCode} errorMessage={ErrorMessage} fallbackUsed={FallbackUsed} correlationId={CorrelationId} validationPath={ValidationPath}",
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
                logEvent.ValidationPath);
            return;
        }

        logger.LogInformation(
            "HarnessEvent status={Status} provider={Provider} model={Model} attempts={Attempts} retryCount={RetryCount} durationMs={DurationMs} success={Success} errorType={ErrorType} errorCode={ErrorCode} errorMessage={ErrorMessage} fallbackUsed={FallbackUsed} correlationId={CorrelationId} validationPath={ValidationPath} requestPayload={RequestPayload} responsePayload={ResponsePayload} normalizedResponse={NormalizedResponse} outputSchema={OutputSchema}",
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
            LlmPayloadRedactor.Redact(logEvent.RequestPayload),
            LlmPayloadRedactor.Redact(logEvent.ResponsePayload ?? logEvent.RawResponse),
            LlmPayloadRedactor.Redact(logEvent.NormalizedResponse),
            LlmPayloadRedactor.Redact(logEvent.OutputSchema));
    }
}
