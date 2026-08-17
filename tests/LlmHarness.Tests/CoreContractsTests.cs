using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;

namespace LlmHarness.Tests;

public sealed class CoreContractsTests
{
    [Fact]
    public void Request_preserves_provider_agnostic_configuration()
    {
        var request = new LlmRequest(
            [new LlmMessage(LlmMessageRole.User, "Summarize this text.")],
            model: "demo-model",
            provider: LlmProviderKind.OpenAI,
            executionMode: LlmExecutionMode.Manual,
            timeout: TimeSpan.FromSeconds(10),
            temperature: 0.2,
            maxTokens: 100);

        Assert.Single(request.Messages);
        Assert.Equal(LlmProviderKind.OpenAI, request.Provider);
        Assert.Equal("demo-model", request.Model);
        Assert.Equal(TimeSpan.FromSeconds(10), request.Timeout);
        Assert.Equal(0.2, request.Temperature);
        Assert.Equal(100, request.MaxTokens);
    }

    [Fact]
    public void Result_can_represent_success_and_failure()
    {
        var success = LlmResult<string>.CreateSuccess(
            "answer",
            new LlmMetadata { AttemptCount = 1 });
        var failure = LlmResult<string>.CreateFailure(
            new LlmError(LlmErrorType.ProviderError, "Provider failed.", Retryable: true));

        Assert.True(success.Success);
        Assert.Equal("answer", success.Output);
        Assert.Null(success.Error);
        Assert.False(failure.Success);
        Assert.Null(failure.Output);
        Assert.Equal(LlmErrorType.ProviderError, failure.Error!.Type);
        Assert.True(failure.Error.Retryable);
    }
}
