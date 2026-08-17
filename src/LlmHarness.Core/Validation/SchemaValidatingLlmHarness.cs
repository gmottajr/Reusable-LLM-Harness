using System.Text.Json;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Harness;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Schema;

namespace LlmHarness.Core.Validation;

public sealed class SchemaValidatingLlmHarness : ILlmHarness
{
    private readonly ILlmHarness _innerHarness;
    private readonly ISchemaValidator _schemaValidator;
    private readonly ILlmHarnessLogger? _logger;

    public SchemaValidatingLlmHarness(
        ILlmHarness innerHarness,
        ISchemaValidator schemaValidator,
        ILlmHarnessLogger? logger = null)
    {
        _innerHarness = innerHarness ?? throw new ArgumentNullException(nameof(innerHarness));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _logger = logger;
    }

    public async Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerHarness.ExecuteAsync<TOutput>(request, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(request.OutputSchema))
        {
            return result;
        }

        string rawResponse;
        try
        {
            rawResponse = SerializeOutput(result.Output);
        }
        catch (JsonException)
        {
            return OutputValidationFailure<TOutput>(result.Metadata, "$", "The LLM response could not be serialized for schema validation.");
        }
        catch (NotSupportedException)
        {
            return OutputValidationFailure<TOutput>(result.Metadata, "$", "The LLM response could not be serialized for schema validation.");
        }

        var responseJson = NormalizeForValidation(rawResponse);
        var validation = _schemaValidator.Validate(responseJson, request.OutputSchema);
        if (validation.IsValid)
        {
            return result;
        }

        var firstError = validation.Errors[0];
        LogValidationFailure(
            result.Metadata,
            result.Metadata.RawResponse ?? rawResponse,
            responseJson == rawResponse ? null : responseJson,
            request.OutputSchema,
            firstError.Path);
        return OutputValidationFailure<TOutput>(
            result.Metadata,
            firstError.Path,
            $"The LLM response did not match the expected schema at {firstError.Path}: {firstError.Message}");
    }

    private static string SerializeOutput<TOutput>(TOutput? output) =>
        output switch
        {
            string rawJson => rawJson,
            JsonElement jsonElement => jsonElement.GetRawText(),
            _ => JsonSerializer.Serialize(output)
        };

    private static string NormalizeForValidation(string rawResponse) =>
        JsonResponseNormalizer.TryParse(rawResponse, out var normalized)
            ? normalized.GetRawText()
            : rawResponse;

    private void LogValidationFailure(
        LlmMetadata metadata,
        string rawResponse,
        string? normalizedResponse,
        string outputSchema,
        string validationPath)
    {
        if (_logger is null)
        {
            return;
        }

        try
        {
            _logger.Log(new LlmLogEvent
            {
                CorrelationId = metadata.CorrelationId ?? "unknown",
                Status = "output_validation_failed",
                Provider = metadata.SelectedProvider ?? metadata.Provider,
                Model = metadata.Model,
                Success = false,
                ErrorType = LlmErrorType.OutputValidationError,
                RawResponse = rawResponse,
                NormalizedResponse = normalizedResponse,
                OutputSchema = outputSchema,
                ValidationPath = validationPath
            });
        }
        catch
        {
            // Diagnostics must never change the provider result.
        }
    }

    private static LlmResult<TOutput> OutputValidationFailure<TOutput>(
        LlmMetadata metadata,
        string code,
        string message) =>
        LlmResult<TOutput>.CreateFailure(
            new LlmError(LlmErrorType.OutputValidationError, message, Retryable: false, Code: code),
            metadata);
}
