using System.Text.Json;
using LlmHarness.Api.Models;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;

namespace LlmHarness.Api.Mapping;

public static class ApiRequestMapper
{
    public static bool TryMap(
        ApiLlmCompleteRequest? apiRequest,
        out LlmRequest? request,
        out ApiErrorResponse? error)
    {
        request = null;
        error = null;

        if (apiRequest is null)
        {
            error = Invalid("Request body is required.", "request");
            return false;
        }

        if (apiRequest.Messages is null || apiRequest.Messages.Count == 0)
        {
            error = Invalid("At least one message is required.", "messages");
            return false;
        }

        if (!TryParseProvider(apiRequest.Provider, out var provider, out error) ||
            !TryParseExecutionMode(apiRequest.ExecutionMode, out var executionMode, out error) ||
            !TryParseMessages(apiRequest.Messages, out var messages, out error) ||
            !TryParseTimeout(apiRequest.TimeoutMs, out var timeout, out error) ||
            !TryParseSchema(apiRequest.OutputSchema, out var outputSchema, out error))
        {
            return false;
        }

        if (apiRequest.Model is not null && string.IsNullOrWhiteSpace(apiRequest.Model))
        {
            error = Invalid("Model cannot be empty when provided.", "model");
            return false;
        }

        request = new LlmRequest(
            messages,
            apiRequest.Model,
            provider,
            executionMode,
            timeout,
            apiRequest.Temperature,
            apiRequest.MaxTokens,
            outputSchema);
        return true;
    }

    private static bool TryParseProvider(
        string? value,
        out LlmProviderKind? provider,
        out ApiErrorResponse? error)
    {
        provider = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<LlmProviderKind>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            provider = parsed;
            return true;
        }

        error = Invalid($"Unsupported provider '{value}'.", "provider");
        return false;
    }

    private static bool TryParseExecutionMode(
        string? value,
        out LlmExecutionMode? executionMode,
        out ApiErrorResponse? error)
    {
        executionMode = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<LlmExecutionMode>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            executionMode = parsed;
            return true;
        }

        error = Invalid($"Unsupported execution mode '{value}'.", "executionMode");
        return false;
    }

    private static bool TryParseMessages(
        IReadOnlyList<ApiLlmMessage?> apiMessages,
        out IReadOnlyList<LlmMessage> messages,
        out ApiErrorResponse? error)
    {
        var mapped = new List<LlmMessage>(apiMessages.Count);
        for (var index = 0; index < apiMessages.Count; index++)
        {
            var apiMessage = apiMessages[index];
            if (apiMessage is null || string.IsNullOrWhiteSpace(apiMessage.Role))
            {
                messages = [];
                error = Invalid($"Message at index {index} must include a role.", "messages");
                return false;
            }

            if (string.IsNullOrWhiteSpace(apiMessage.Content))
            {
                messages = [];
                error = Invalid($"Message at index {index} must include content.", "messages");
                return false;
            }

            if (!Enum.TryParse<LlmMessageRole>(apiMessage.Role, ignoreCase: true, out var role) ||
                !Enum.IsDefined(role))
            {
                messages = [];
                error = Invalid($"Unsupported message role '{apiMessage.Role}'.", "messages");
                return false;
            }

            mapped.Add(new LlmMessage(role, apiMessage.Content));
        }

        messages = mapped;
        error = null;
        return true;
    }

    private static bool TryParseTimeout(
        long? timeoutMs,
        out TimeSpan? timeout,
        out ApiErrorResponse? error)
    {
        timeout = null;
        error = null;

        if (!timeoutMs.HasValue)
        {
            return true;
        }

        if (timeoutMs.Value <= 0 || timeoutMs.Value > TimeSpan.MaxValue.TotalMilliseconds)
        {
            error = Invalid("timeoutMs must be greater than zero and within the supported range.", "timeoutMs");
            return false;
        }

        timeout = TimeSpan.FromMilliseconds(timeoutMs.Value);
        return true;
    }

    private static bool TryParseSchema(
        JsonElement? schema,
        out string? outputSchema,
        out ApiErrorResponse? error)
    {
        outputSchema = null;
        error = null;

        if (!schema.HasValue)
        {
            return true;
        }

        if (schema.Value.ValueKind != JsonValueKind.Object)
        {
            error = Invalid("outputSchema must be a JSON object.", "outputSchema");
            return false;
        }

        outputSchema = schema.Value.GetRawText();
        return true;
    }

    private static ApiErrorResponse Invalid(string message, string code) =>
        new(
            LlmErrorType.InputValidationError.ToString(),
            message,
            Retryable: false,
            code);
}
