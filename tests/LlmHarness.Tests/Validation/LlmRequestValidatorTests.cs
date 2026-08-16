using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Validation;

namespace LlmHarness.Tests.Validation;

public sealed class LlmRequestValidatorTests
{
    private readonly LlmRequestValidator _validator = new();

    [Fact]
    public void Valid_request_is_accepted()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_messages_are_rejected()
    {
        var result = _validator.Validate(ValidRequest(messages: []));

        AssertError(result, "messages", "At least one message is required.");
    }

    [Fact]
    public void Invalid_role_and_empty_content_are_rejected()
    {
        var request = ValidRequest(
            messages: [new((LlmMessageRole)999, " ")]);

        var result = _validator.Validate(request);

        AssertError(result, "messages[0].role");
        AssertError(result, "messages[0].content");
    }

    [Fact]
    public void Non_positive_timeout_is_rejected()
    {
        var result = _validator.Validate(ValidRequest(timeout: TimeSpan.Zero));

        AssertError(result, "timeout", "Timeout must be positive.");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void Temperature_outside_supported_range_is_rejected(double temperature)
    {
        var result = _validator.Validate(ValidRequest(temperature: temperature));

        AssertError(result, "temperature");
    }

    [Fact]
    public void Non_positive_max_tokens_are_rejected()
    {
        var result = _validator.Validate(ValidRequest(maxTokens: 0));

        AssertError(result, "maxTokens");
    }

    [Fact]
    public void Manual_mode_requires_a_valid_provider_and_model()
    {
        var missingProvider = _validator.Validate(
            new LlmRequest(
                [new(LlmMessageRole.User, "Hello")],
                model: null,
                executionMode: LlmExecutionMode.Manual));
        var missingModel = _validator.Validate(
            new LlmRequest(
                [new(LlmMessageRole.User, "Hello")],
                provider: LlmProviderKind.OpenAI));
        var invalidProvider = _validator.Validate(
            ValidRequest(provider: (LlmProviderKind)999));

        AssertError(missingProvider, "provider");
        AssertError(missingModel, "model");
        AssertError(invalidProvider, "provider", "Provider selection is invalid.");
    }

    [Fact]
    public void Invalid_output_schema_is_rejected()
    {
        var malformed = _validator.Validate(ValidRequest(outputSchema: "{not-json"));
        var nonObject = _validator.Validate(ValidRequest(outputSchema: "[]"));

        AssertError(malformed, "outputSchema");
        AssertError(nonObject, "outputSchema", "Output schema must be a JSON object.");
    }

    [Fact]
    public void Validation_errors_are_structured_and_not_retryable()
    {
        var result = _validator.Validate(ValidRequest(maxTokens: -1));

        var error = Assert.Single(result.Errors);
        Assert.Equal(LlmErrorType.InputValidationError, error.Type);
        Assert.False(error.Retryable);
        Assert.Equal("maxTokens", error.Code);
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_inner_harness_is_called()
    {
        var innerHarness = new RecordingHarness();
        var harness = new ValidatingLlmHarness(innerHarness, _validator);

        var result = await harness.ExecuteAsync<string>(ValidRequest(maxTokens: 0));

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.InputValidationError, result.Error!.Type);
        Assert.False(innerHarness.WasCalled);
    }

    [Fact]
    public async Task Valid_request_is_delegated_to_inner_harness()
    {
        var innerHarness = new RecordingHarness();
        var harness = new ValidatingLlmHarness(innerHarness, _validator);

        var result = await harness.ExecuteAsync<string>(ValidRequest());

        Assert.True(result.Success);
        Assert.True(innerHarness.WasCalled);
    }

    private static LlmRequest ValidRequest(
        IReadOnlyList<LlmMessage>? messages = null,
        LlmProviderKind? provider = LlmProviderKind.OpenAI,
        string? model = "demo-model",
        TimeSpan? timeout = null,
        double? temperature = 0.2,
        int? maxTokens = 100,
        string? outputSchema = null) =>
        new(
            messages ?? [new(LlmMessageRole.User, "Hello")],
            model,
            provider,
            LlmExecutionMode.Manual,
            timeout ?? TimeSpan.FromSeconds(30),
            temperature,
            maxTokens,
            outputSchema);

    private static void AssertError(
        LlmValidationResult result,
        string code,
        string? message = null)
    {
        var error = Assert.Single(result.Errors.Where(error => error.Code == code));
        Assert.False(result.IsValid);
        Assert.Equal(LlmErrorType.InputValidationError, error.Type);
        Assert.False(error.Retryable);
        if (message is not null)
        {
            Assert.Equal(message, error.Message);
        }
    }

    private sealed class RecordingHarness : ILlmHarness
    {
        public bool WasCalled { get; private set; }

        public Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
            LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(
                LlmResult<TOutput>.CreateSuccess(default!));
        }
    }
}
