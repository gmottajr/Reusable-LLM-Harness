using System.Text.Json;
using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Core.Harness;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Policies;

namespace LlmHarness.Tests.Harness;

public sealed class LlmHarnessTests
{
    [Fact]
    public async Task Success_path_returns_typed_output_and_safe_log_events()
    {
        var provider = new FakeProvider(
            LlmProviderKind.OpenAI,
            (_, _) => Response("{\"answer\":\"ok\"}"));
        var logger = new RecordingLogger();
        var harness = CreateHarness([provider], logger: logger);

        var result = await harness.ExecuteAsync<Dictionary<string, string>>(Request());

        Assert.True(result.Success);
        Assert.Equal("ok", result.Output!["answer"]);
        Assert.Equal(LlmProviderKind.OpenAI, result.Metadata.SelectedProvider);
        Assert.Equal("{\"answer\":\"ok\"}", result.Metadata.RawResponse);
        Assert.False(string.IsNullOrWhiteSpace(result.Metadata.CorrelationId));
        Assert.Equal(1, provider.CompleteCalls);
        Assert.Contains(logger.Events, logEvent => logEvent.Status == "started");
        Assert.Contains(logger.Events, logEvent => logEvent.Status == "success");
        Assert.All(logger.Events, logEvent => Assert.DoesNotContain("answer", logEvent.Status));
    }

    [Fact]
    public async Task Input_validation_failure_does_not_call_a_provider()
    {
        var provider = new FakeProvider(
            LlmProviderKind.OpenAI,
            (_, _) => Response("{\"answer\":\"never\"}"));
        var harness = CreateHarness([provider]);

        var result = await harness.ExecuteAsync<string>(Request(messages: []));

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.InputValidationError, result.Error!.Type);
        Assert.Equal(0, provider.CompleteCalls);
    }

    [Fact]
    public async Task Unavailable_provider_returns_structured_failure()
    {
        var provider = new FakeProvider(
            LlmProviderKind.OpenAI,
            (_, _) => Response("unused"))
        {
            Available = false
        };
        var harness = CreateHarness([provider]);

        var result = await harness.ExecuteAsync<string>(Request());

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.ProviderUnavailable, result.Error!.Type);
        Assert.Equal(0, provider.CompleteCalls);
    }

    [Fact]
    public async Task Auto_prefer_local_selects_a_local_provider_first()
    {
        var cloud = new FakeProvider(
            LlmProviderKind.OpenAI,
            (_, _) => Response("cloud"));
        var local = new FakeProvider(
            LlmProviderKind.Ollama,
            (_, _) => Response("local"));
        var harness = CreateHarness([cloud, local]);

        var result = await harness.ExecuteAsync<string>(
            Request(
                provider: null,
                executionMode: LlmExecutionMode.AutoPreferLocal));

        Assert.True(result.Success);
        Assert.Equal("local", result.Output);
        Assert.Equal(LlmProviderKind.Ollama, result.Metadata.SelectedProvider);
        Assert.Equal(0, cloud.CompleteCalls);
        Assert.Equal(1, local.CompleteCalls);
    }

    [Fact]
    public async Task Provider_failure_is_retried_and_then_succeeds()
    {
        var calls = 0;
        var provider = new FakeProvider(
            LlmProviderKind.OpenAI,
            (_, _) =>
            {
                calls++;
                return calls == 1
                    ? Task.FromException<LlmProviderResponse>(
                        new LlmProviderException("temporary failure", statusCode: 500))
                    : Response("done");
            });
        var harness = CreateHarness([provider]);

        var result = await harness.ExecuteAsync<string>(Request());

        Assert.True(result.Success);
        Assert.Equal("done", result.Output);
        Assert.Equal(2, provider.CompleteCalls);
        Assert.Equal(2, result.Metadata.AttemptCount);
        Assert.Equal(1, result.Metadata.RetryCount);
    }

    [Fact]
    public async Task Timeout_returns_structured_failure()
    {
        var provider = new FakeProvider(
            LlmProviderKind.OpenAI,
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                return await Response("too late");
            });
        var harness = CreateHarness(
            [provider],
            timeoutOptions: new LlmTimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(10)
            });

        var result = await harness.ExecuteAsync<string>(Request());

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.TimeoutError, result.Error!.Type);
        Assert.False(result.Metadata.FallbackUsed);
    }

    [Fact]
    public async Task Timeout_uses_configured_fallback_provider()
    {
        var primary = new FakeProvider(
            LlmProviderKind.OpenAI,
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                return await Response("too late");
            });
        var fallback = new FakeProvider(
            LlmProviderKind.Ollama,
            (_, _) => Response("fallback answer"));
        var harness = CreateHarness(
            [primary, fallback],
            timeoutOptions: new LlmTimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromMilliseconds(10)
            },
            fallbackProviderKind: LlmProviderKind.Ollama);

        var result = await harness.ExecuteAsync<string>(Request());

        Assert.True(result.Success);
        Assert.Equal("fallback answer", result.Output);
        Assert.True(result.Metadata.FallbackUsed);
        Assert.Equal(LlmProviderKind.Ollama, result.Metadata.SelectedProvider);
        Assert.Equal(1, fallback.CompleteCalls);
    }

    [Fact]
    public async Task Output_schema_failure_returns_structured_failure()
    {
        var provider = new FakeProvider(
            LlmProviderKind.OpenAI,
            (_, _) => Response("{\"answer\":42}"));
        var logger = new RecordingLogger();
        var harness = CreateHarness([provider], logger: logger);
        var request = Request(outputSchema: """
            {
              "type": "object",
              "required": ["answer"],
              "properties": { "answer": { "type": "string" } }
            }
            """);

        var result = await harness.ExecuteAsync<string>(request);

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.OutputValidationError, result.Error!.Type);
        Assert.Equal("$.answer", result.Error.Code);
        var diagnostic = Assert.Single(
            logger.Events,
            logEvent => logEvent.Status == "output_validation_failed");
        Assert.Equal("{\"answer\":42}", diagnostic.RawResponse);
        Assert.Equal(request.OutputSchema, diagnostic.OutputSchema);
        Assert.Equal("$.answer", diagnostic.ValidationPath);
    }

    [Theory]
    [InlineData("```json\n{\"answer\":\"ok\"}\n```", "Object")]
    [InlineData("Here is the result:\n[1, true, null]\nDone.", "Array")]
    [InlineData("The answer is: 42", "Number")]
    [InlineData("Result: false", "False")]
    [InlineData("The value is null.", "Null")]
    [InlineData("The value is \"ready\".", "String")]
    public async Task Structured_output_accepts_any_json_root_and_common_llm_wrappers(
        string providerContent,
        string expectedKind)
    {
        var provider = new FakeProvider(
            LlmProviderKind.OpenAI,
            (_, _) => Response(providerContent));
        var harness = CreateHarness([provider]);

        var result = await harness.ExecuteAsync<JsonElement>(Request());

        Assert.True(result.Success);
        Assert.Equal(Enum.Parse<JsonValueKind>(expectedKind), result.Output!.ValueKind);
    }

    [Fact]
    public async Task Structured_output_reports_a_parsing_error_only_when_no_json_value_exists()
    {
        var provider = new FakeProvider(
            LlmProviderKind.OpenAI,
            (_, _) => Response("I cannot provide structured data."));
        var harness = CreateHarness([provider]);

        var result = await harness.ExecuteAsync<JsonElement>(Request());

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.OutputParsingError, result.Error!.Type);
        Assert.Contains("valid JSON value", result.Error.Message);
    }

    private static LlmHarness.Core.Harness.LlmHarness CreateHarness(
        IReadOnlyList<ILlmProvider> providers,
        RecordingLogger? logger = null,
        LlmTimeoutOptions? timeoutOptions = null,
        LlmProviderKind? fallbackProviderKind = null) =>
        new(
            providers,
            retryPolicy: new LlmRetryPolicy(
                new LlmRetryOptions
                {
                    MaxRetries = 2,
                    UseJitter = false
                },
                delayAsync: (_, _) => Task.CompletedTask),
            timeoutOptions: timeoutOptions ?? new LlmTimeoutOptions
            {
                DefaultTimeout = TimeSpan.FromSeconds(1)
            },
            logger: logger,
            fallbackProviderKind: fallbackProviderKind);

    private static LlmRequest Request(
        IReadOnlyList<LlmMessage>? messages = null,
        string? outputSchema = null,
        LlmProviderKind? provider = LlmProviderKind.OpenAI,
        LlmExecutionMode executionMode = LlmExecutionMode.Manual) =>
        new(
            messages ?? [new(LlmMessageRole.User, "Hello")],
            model: "demo-model",
            provider: provider,
            executionMode: executionMode,
            outputSchema: outputSchema);

    private static Task<LlmProviderResponse> Response(string content) =>
        Task.FromResult(
            new LlmProviderResponse(
                content,
                LlmProviderKind.OpenAI,
                model: "demo-model",
                providerRequestId: "request-1"));

    private sealed class FakeProvider(
        LlmProviderKind kind,
        Func<LlmProviderRequest, CancellationToken, Task<LlmProviderResponse>> complete) : ILlmProvider
    {
        public LlmProviderKind Kind { get; } = kind;

        public bool Available { get; init; } = true;

        public int CompleteCalls { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Available);

        public async Task<LlmProviderResponse> CompleteAsync(
            LlmProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return await complete(request, cancellationToken);
        }
    }

    private sealed class RecordingLogger : ILlmHarnessLogger
    {
        public List<LlmLogEvent> Events { get; } = [];

        public void Log(LlmLogEvent logEvent) => Events.Add(logEvent);
    }
}
