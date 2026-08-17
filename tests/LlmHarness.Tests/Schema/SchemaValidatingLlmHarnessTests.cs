using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Interfaces;
using LlmHarness.Core.Schema;
using LlmHarness.Core.Validation;

namespace LlmHarness.Tests.Schema;

public sealed class SchemaValidatingLlmHarnessTests
{
    [Fact]
    public async Task Valid_output_is_returned_when_it_matches_the_schema()
    {
        var harness = CreateHarness("{\"answer\":\"yes\"}");
        var request = RequestWithSchema("""
            {
              "type": "object",
              "required": ["answer"],
              "properties": { "answer": { "type": "string" } }
            }
            """);

        var result = await harness.ExecuteAsync<string>(request);

        Assert.True(result.Success);
        Assert.Equal("{\"answer\":\"yes\"}", result.Output);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Invalid_output_returns_structured_output_validation_error()
    {
        var harness = CreateHarness("{\"answer\":42}");
        var request = RequestWithSchema("""
            {
              "type": "object",
              "required": ["answer"],
              "properties": { "answer": { "type": "string" } }
            }
            """);

        var result = await harness.ExecuteAsync<string>(request);

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.OutputValidationError, result.Error!.Type);
        Assert.False(result.Error.Retryable);
        Assert.Equal("$.answer", result.Error.Code);
        Assert.Contains("did not match", result.Error.Message);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Json_wrapped_in_markdown_is_normalized_before_schema_validation()
    {
        var harness = CreateHarness("```json\n{\"answer\":\"yes\"}\n```");
        var request = RequestWithSchema("""
            {
              "type": "object",
              "required": ["answer"],
              "properties": { "answer": { "type": "string" } }
            }
            """);

        var result = await harness.ExecuteAsync<string>(request);

        Assert.True(result.Success);
        Assert.Equal("```json\n{\"answer\":\"yes\"}\n```", result.Output);
    }

    [Fact]
    public async Task Malformed_json_returns_structured_output_validation_error()
    {
        var harness = CreateHarness("{\"answer\":");
        var request = RequestWithSchema("{\"type\":\"object\"}");

        var result = await harness.ExecuteAsync<string>(request);

        Assert.False(result.Success);
        Assert.Equal(LlmErrorType.OutputValidationError, result.Error!.Type);
        Assert.False(result.Error.Retryable);
        Assert.Equal("$", result.Error.Code);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Missing_schema_skips_output_validation()
    {
        var harness = CreateHarness("not-json");
        var request = RequestWithSchema(null);

        var result = await harness.ExecuteAsync<string>(request);

        Assert.True(result.Success);
        Assert.Equal("not-json", result.Output);
    }

    private static SchemaValidatingLlmHarness CreateHarness(string output) =>
        new(new FixedOutputHarness(output), new JsonSchemaValidator());

    private static LlmRequest RequestWithSchema(string? schema) =>
        new(
            [new(LlmMessageRole.User, "Answer the question.")],
            model: "demo-model",
            provider: LlmProviderKind.OpenAI,
            outputSchema: schema);

    private sealed class FixedOutputHarness(string output) : ILlmHarness
    {
        public Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
            LlmRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LlmResult<TOutput>.CreateSuccess((TOutput)(object)output));
    }
}
