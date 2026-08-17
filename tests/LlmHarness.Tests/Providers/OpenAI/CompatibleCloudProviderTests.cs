using System.Net;
using System.Text.Json;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Exceptions;
using LlmHarness.Providers.OpenAI;

namespace LlmHarness.Tests.Providers.OpenAI;

public sealed class CompatibleCloudProviderTests
{
    [Theory]
    [InlineData(LlmProviderKind.Mistral, "mistral-small-latest", "https://api.mistral.ai/v1/chat/completions")]
    [InlineData(LlmProviderKind.Grok, "grok-4.5", "https://api.x.ai/v1/chat/completions")]
    public async Task Provider_maps_openai_compatible_cloud_requests(
        LlmProviderKind kind,
        string model,
        string endpoint)
    {
        var handler = new StubHandler(
            """
            {
              "id": "cloud-1",
              "choices": [{
                "message": { "content": "Cloud response" },
                "finish_reason": "stop"
              }],
              "usage": { "prompt_tokens": 4, "completion_tokens": 2 }
            }
            """);
        using var client = new HttpClient(handler);
        var provider = new CompatibleCloudProvider(
            client,
            new CompatibleCloudProviderOptions
            {
                Provider = kind,
                ApiKey = "cloud-test-key",
                Endpoint = endpoint,
                DefaultModel = model
            });

        var result = await provider.CompleteAsync(
            new LlmProviderRequest(
                [new(LlmMessageRole.User, "Hello")],
                kind));

        Assert.Equal("Cloud response", result.Content);
        Assert.Equal(kind, result.Provider);
        Assert.Equal(model, result.Model);
        Assert.Equal("Bearer cloud-test-key", handler.Request!.Headers.Authorization!.ToString());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(model, body.RootElement.GetProperty("model").GetString());
        Assert.Equal("Hello", body.RootElement.GetProperty("messages")[0]
            .GetProperty("content").GetString());
    }

    [Fact]
    public async Task Provider_preserves_xai_style_error_details()
    {
        var handler = new StubHandler(
            "{\"code\":\"forbidden\",\"error\":\"The API key does not have chat permission.\"}",
            HttpStatusCode.Forbidden);
        using var client = new HttpClient(handler);
        var provider = new CompatibleCloudProvider(
            client,
            new CompatibleCloudProviderOptions
            {
                Provider = LlmProviderKind.Grok,
                ApiKey = "cloud-test-key",
                Endpoint = "https://api.x.ai/v1/chat/completions",
                DefaultModel = "grok-4.5"
            });

        var exception = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.CompleteAsync(
                new LlmProviderRequest(
                    [new(LlmMessageRole.User, "Hello")],
                    LlmProviderKind.Grok)));

        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("forbidden", exception.ProviderCode);
        Assert.Equal("The API key does not have chat permission.", exception.Message);
    }

    private sealed class StubHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}
