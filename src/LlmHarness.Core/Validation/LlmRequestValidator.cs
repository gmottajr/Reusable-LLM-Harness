using System.Text.Json;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Validation;

public sealed class LlmRequestValidator : ILlmRequestValidator
{
    public const double MinimumTemperature = 0;
    public const double MaximumTemperature = 2;

    public LlmValidationResult Validate(LlmRequest? request)
    {
        if (request is null)
        {
            return LlmValidationResult.Invalid(
            [
                Error("request", "A request is required.")
            ]);
        }

        var errors = new List<LlmError>();

        ValidateMessages(request, errors);
        ValidateExecutionMode(request, errors);
        ValidateProvider(request, errors);
        ValidateModel(request, errors);
        ValidateTimeout(request, errors);
        ValidateTemperature(request, errors);
        ValidateMaxTokens(request, errors);
        ValidateOutputSchema(request, errors);

        return errors.Count == 0
            ? LlmValidationResult.Valid()
            : LlmValidationResult.Invalid(errors);
    }

    private static void ValidateMessages(LlmRequest request, ICollection<LlmError> errors)
    {
        if (request.Messages.Count == 0)
        {
            errors.Add(Error("messages", "At least one message is required."));
            return;
        }

        for (var index = 0; index < request.Messages.Count; index++)
        {
            var message = request.Messages[index];
            if (message is null)
            {
                errors.Add(Error($"messages[{index}]", "A message is required."));
                continue;
            }

            if (!Enum.IsDefined(message.Role))
            {
                errors.Add(Error(
                    $"messages[{index}].role",
                    "Message role must be system, user, assistant, or tool."));
            }

            if (string.IsNullOrWhiteSpace(message.Content))
            {
                errors.Add(Error(
                    $"messages[{index}].content",
                    "Message content cannot be empty."));
            }
        }
    }

    private static void ValidateExecutionMode(LlmRequest request, ICollection<LlmError> errors)
    {
        if (!Enum.IsDefined(request.ExecutionMode))
        {
            errors.Add(Error("executionMode", "Execution mode is invalid."));
        }
    }

    private static void ValidateProvider(LlmRequest request, ICollection<LlmError> errors)
    {
        if (request.Provider.HasValue && !Enum.IsDefined(request.Provider.Value))
        {
            errors.Add(Error("provider", "Provider selection is invalid."));
        }

        if (request.ExecutionMode == LlmExecutionMode.Manual &&
            request.HasExecutionModeOverride &&
            !request.Provider.HasValue)
        {
            errors.Add(Error(
                "provider",
                "A provider is required when execution mode is Manual."));
        }
    }

    private static void ValidateModel(LlmRequest request, ICollection<LlmError> errors)
    {
        if (request.Model is not null && string.IsNullOrWhiteSpace(request.Model))
        {
            errors.Add(Error("model", "Model name cannot be empty."));
        }

        if (request.Provider.HasValue &&
            Enum.IsDefined(request.Provider.Value) &&
            request.Model is null)
        {
            errors.Add(Error(
                "model",
                "A model name is required when a provider is selected."));
        }
    }

    private static void ValidateTimeout(LlmRequest request, ICollection<LlmError> errors)
    {
        if (request.Timeout <= TimeSpan.Zero)
        {
            errors.Add(Error("timeout", "Timeout must be positive."));
        }
    }

    private static void ValidateTemperature(LlmRequest request, ICollection<LlmError> errors)
    {
        if (request.Temperature is not { } temperature)
        {
            return;
        }

        if (!double.IsFinite(temperature) ||
            temperature < MinimumTemperature ||
            temperature > MaximumTemperature)
        {
            errors.Add(Error(
                "temperature",
                $"Temperature must be between {MinimumTemperature} and {MaximumTemperature}."));
        }
    }

    private static void ValidateMaxTokens(LlmRequest request, ICollection<LlmError> errors)
    {
        if (request.MaxTokens is <= 0)
        {
            errors.Add(Error("maxTokens", "Max tokens must be positive when provided."));
        }
    }

    private static void ValidateOutputSchema(LlmRequest request, ICollection<LlmError> errors)
    {
        if (request.OutputSchema is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.OutputSchema))
        {
            errors.Add(Error("outputSchema", "Output schema cannot be empty."));
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(request.OutputSchema);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(Error("outputSchema", "Output schema must be a JSON object."));
            }
        }
        catch (JsonException)
        {
            errors.Add(Error("outputSchema", "Output schema must contain valid JSON."));
        }
    }

    private static LlmError Error(string code, string message) =>
        new(LlmErrorType.InputValidationError, message, Retryable: false, Code: code);
}
