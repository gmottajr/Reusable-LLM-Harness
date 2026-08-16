using System.Net;
using System.Text.Json;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Providers.OpenAI;

namespace LlmHarness.Tests.Providers.OpenAI;

public sealed class OpenAiProviderTests
{
    [Fact]
    public async Task Available_provider_maps_request_and_response()
    {
        var handler = new StubHandler(_ => Response(
            HttpStatusCode.OK,
            """
            {
              "id": "chatcmpl-test",
              "choices": [
                {
                  "message": { "role": "assistant", "content": "Hello from OpenAI" },
                  "finish_reason": "stop"
                }
              ],
              "usage": { "prompt_tokens": 5, "completion_tokens": 3 }
            }
            """));
        using var client = new HttpClient(handler);
        var provider = new OpenAiProvider(
            client,
            new OpenAiOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://example.test/v1/chat/completions"
            });

        var available = await provider.IsAvailableAsync();
        var response = await provider.CompleteAsync(Request());

        Assert.True(available);
        Assert.Equal(LlmProviderKind.OpenAI, response.Provider);
        Assert.Equal("Hello from OpenAI", response.Content);
        Assert.Equal("chatcmpl-test", response.ProviderRequestId);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal(5, response.PromptTokens);
        Assert.Equal(3, response.CompletionTokens);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "Bearer test-key",
            handler.Request.Headers.Authorization!.ToString());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("gpt-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            "user",
            body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal(
            "Hello",
            body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(0.3, body.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(50, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Missing_api_key_reports_unavailable_without_network_call()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, "{}"));
        using var client = new HttpClient(handler);
        var provider = new OpenAiProvider(
            client,
            new OpenAiOptions
            {
                ApiKey = string.Empty,
                Endpoint = "https://example.test/v1/chat/completions"
            });

        var available = await provider.IsAvailableAsync();

        Assert.False(available);
        Assert.Equal(
            "Missing OPENAI_API_KEY environment variable.",
            provider.AvailabilityReason);
        Assert.Null(handler.Request);
        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.CompleteAsync(Request()));
        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("missing_api_key", exception.ProviderCode);
        Assert.DoesNotContain("test-key", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error")]
    [InlineData(HttpStatusCode.InternalServerError, "server_error")]
    public async Task Provider_http_errors_are_normalized(
        HttpStatusCode statusCode,
        string providerCode)
    {
        var handler = new StubHandler(_ => Response(
            statusCode,
            $$"""
            {
              "error": {
                "message": "provider failed",
                "type": "{{providerCode}}",
                "code": "{{providerCode}}"
              }
            }
            """));
        using var client = new HttpClient(handler);
        var provider = new OpenAiProvider(
            client,
            new OpenAiOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://example.test/v1/chat/completions"
            });

        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.CompleteAsync(Request()));

        Assert.Equal((int)statusCode, exception.StatusCode);
        Assert.Equal(providerCode, exception.ProviderCode);
        Assert.Equal("provider failed", exception.Message);
    }

    [Fact]
    public async Task Provider_error_message_redacts_the_api_key()
    {
        var handler = new StubHandler(_ => Response(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"key test-key is invalid\",\"code\":\"bad_request\"}}"));
        using var client = new HttpClient(handler);
        var provider = new OpenAiProvider(
            client,
            new OpenAiOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://example.test/v1/chat/completions"
            });

        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.CompleteAsync(Request()));

        Assert.DoesNotContain("test-key", exception.Message);
        Assert.Contains("[REDACTED]", exception.Message);
    }

    private static LlmProviderRequest Request() =>
        new(
            [new(LlmMessageRole.User, "Hello")],
            LlmProviderKind.OpenAI,
            model: "gpt-test",
            temperature: 0.3,
            maxTokens: 50);

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string body) =>
        new(statusCode)
        {
            Content = new StringContent(body)
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
