using System.Net;
using System.Text.Json;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Providers.OpenAI;

namespace LlmHarness.Tests.Providers.OpenAI;

public sealed class GoogleGeminiProviderTests
{
    [Fact]
    public async Task Provider_maps_gemini_request_and_response()
    {
        var handler = new StubHandler(
            """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [{ "text": "Gemini response" }]
                  },
                  "finishReason": "STOP"
                }
              ]
            }
            """);
        using var client = new HttpClient(handler);
        var provider = new GoogleGeminiProvider(
            client,
            new GoogleGeminiOptions
            {
                ApiKey = "google-test-key",
                Endpoint = "https://example.test/v1beta",
                DefaultModel = "gemini-flash-latest"
            });

        var result = await provider.CompleteAsync(
            new LlmProviderRequest(
                [
                    new(LlmMessageRole.System, "Be concise."),
                    new(LlmMessageRole.User, "Hello")
                ],
                LlmProviderKind.GoogleGemini,
                outputSchema: "{\"type\":\"object\"}"));

        Assert.Equal("Gemini response", result.Content);
        Assert.Equal(LlmProviderKind.GoogleGemini, result.Provider);
        Assert.Equal("gemini-flash-latest", result.Model);
        Assert.Equal("google-test-key", handler.Request!.Headers.GetValues("X-goog-api-key").Single());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("Hello", body.RootElement.GetProperty("contents")[0]
            .GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("Be concise.", body.RootElement.GetProperty("systemInstruction")
            .GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig")
            .GetProperty("responseMimeType").GetString());
        Assert.EndsWith(
            "/models/gemini-flash-latest:generateContent",
            handler.Request.RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
    }

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}
