using System.Text.Json;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Schema;

namespace LlmHarness.Core.Validation;

public sealed class SchemaValidatingLlmHarness : ILlmHarness
{
    private readonly ILlmHarness _innerHarness;
    private readonly ISchemaValidator _schemaValidator;

    public SchemaValidatingLlmHarness(
        ILlmHarness innerHarness,
        ISchemaValidator schemaValidator)
    {
        _innerHarness = innerHarness ?? throw new ArgumentNullException(nameof(innerHarness));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
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

        string responseJson;
        try
        {
            responseJson = SerializeOutput(result.Output);
        }
        catch (JsonException)
        {
            return OutputValidationFailure(result.Metadata, "$", "The LLM response could not be serialized for schema validation.");
        }
        catch (NotSupportedException)
        {
            return OutputValidationFailure(result.Metadata, "$", "The LLM response could not be serialized for schema validation.");
        }

        var validation = _schemaValidator.Validate(responseJson, request.OutputSchema);
        if (validation.IsValid)
        {
            return result;
        }

        var firstError = validation.Errors[0];
        return OutputValidationFailure(
            result.Metadata,
            firstError.Path,
            "The LLM response did not match the expected schema.");
    }

    private static string SerializeOutput<TOutput>(TOutput? output) =>
        output switch
        {
            string rawJson => rawJson,
            JsonElement jsonElement => jsonElement.GetRawText(),
            _ => JsonSerializer.Serialize(output)
        };

    private static LlmResult<TOutput> OutputValidationFailure<TOutput>(
        LlmMetadata metadata,
        string code,
        string message) =>
        LlmResult<TOutput>.CreateFailure(
            new LlmError(LlmErrorType.OutputValidationError, message, Retryable: false, Code: code),
            metadata);
}
