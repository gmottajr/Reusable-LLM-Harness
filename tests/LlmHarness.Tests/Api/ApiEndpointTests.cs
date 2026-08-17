using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LlmHarness.Tests.Api;

public sealed class ApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"))
            .CreateClient();
    }

    [Fact]
    public async Task Provider_status_returns_availability_without_credentials()
    {
        using var response = await _client.GetAsync("/api/providers/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var configuredKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            Assert.DoesNotContain(configuredKey, body, StringComparison.Ordinal);
        }

        using var document = JsonDocument.Parse(body);
        Assert.Contains(
            document.RootElement.EnumerateArray(),
            item => item.GetProperty("provider").GetString() == "OpenAI");
    }

    [Fact]
    public async Task Setup_sources_exposes_the_three_supported_llm_paths()
    {
        using var response = await _client.GetAsync("/api/setup/sources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = document.RootElement
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .ToArray();

        Assert.Equal(
            ["cloud-api", "managed-local", "installed-local"],
            ids);
    }

    [Fact]
    public async Task Installed_local_setup_returns_safe_connection_settings()
    {
        using var response = await _client.GetAsync("/api/setup/installed-local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("http://127.0.0.1:11434/v1", document.RootElement.GetProperty("endpoint").GetString());
        Assert.Equal("llama3.2", document.RootElement.GetProperty("model").GetString());
        Assert.DoesNotContain("Bearer", document.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Completion_rejects_missing_messages_with_structured_error()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/llm/complete",
            new { model = "gpt-4.1-mini" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "InputValidationError",
            document.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Completion_rejects_unknown_message_roles_before_provider_call()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/llm/complete",
            new
            {
                messages = new[]
                {
                    new { role = "not-a-role", content = "hello" }
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "InputValidationError",
            document.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Completion_rejects_malformed_json_with_structured_error()
    {
        using var content = new StringContent(
            "{\"messages\":[",
            System.Text.Encoding.UTF8,
            "application/json");
        using var response = await _client.PostAsync("/api/llm/complete", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "InputValidationError",
            document.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Completion_with_output_schema_returns_structured_json_on_provider_failure()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/llm/complete",
            new
            {
                provider = "OpenAI",
                model = "gpt-4.1-mini",
                messages = new[]
                {
                    new { role = "user", content = "hello" }
                },
                outputSchema = new
                {
                    type = "object",
                    properties = new { answer = new { type = "string" } }
                }
            });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "ProviderUnavailable",
            document.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Completion_returns_harness_failure_as_structured_http_response()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/llm/complete",
            new
            {
                provider = "Ollama",
                model = "llama3",
                messages = new[]
                {
                    new { role = "user", content = "hello" }
                }
            });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "ProviderUnavailable",
            document.RootElement.GetProperty("error").GetProperty("type").GetString());
        Assert.Equal(
            "Ollama",
            document.RootElement.GetProperty("metadata").GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Managed_models_endpoint_exposes_curated_catalog_status()
    {
        using var response = await _client.GetAsync("/api/models");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var models = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(models, model =>
            model.GetProperty("id").GetString() == "smollm2-135m-instruct-q4km" &&
            model.GetProperty("state").GetString() == "NotDownloaded");
        Assert.Contains(models, model => model.GetProperty("id").GetString() == "qwen2.5-0.5b-instruct-q4km");
        Assert.Contains(models, model =>
            model.GetProperty("id").GetString() == "qwen3-0.6b-q4f16-browser" &&
            model.GetProperty("browserModelId").GetString() == "Qwen3-0.6B-q4f16_1-MLC" &&
            model.GetProperty("browserOnly").GetBoolean());
        Assert.Contains(models, model =>
            model.GetProperty("browserModelId").GetString() == "Qwen2.5-0.5B-Instruct-q4f16_1-MLC" &&
            model.GetProperty("browserOnly").GetBoolean());
        Assert.Contains(models, model => model.GetProperty("browserModelId").GetString() == "gemma3-1b-it-q4f16_1-MLC");
        Assert.Contains(models, model => model.GetProperty("id").GetString() == "deepseek-r1-distill-qwen-1.5b-q4km");
        Assert.Contains(models, model =>
            model.GetProperty("id").GetString() == "deepseek-r1-distill-qwen-7b-q4f16-browser" &&
            model.GetProperty("browserModelId").GetString() == "DeepSeek-R1-Distill-Qwen-7B-q4f16_1-MLC" &&
            model.GetProperty("browserOnly").GetBoolean() &&
            model.GetProperty("browserTier").GetString() == "heavy" &&
            !model.GetProperty("browserRecommended").GetBoolean() &&
            model.GetProperty("browserWarning").GetString()!.Contains("5", StringComparison.Ordinal));
        Assert.Contains(models, model => model.GetProperty("id").GetString() == "gemma-3-1b-it-q4km");
    }

    [Fact]
    public async Task Managed_model_download_rejects_unknown_catalog_ids()
    {
        using var response = await _client.PostAsync(
            "/api/models/not-in-catalog/download",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Browser_only_model_download_is_not_sent_to_backend_runtime()
    {
        using var response = await _client.PostAsync(
            "/api/models/qwen2.5-0.5b-instruct-q4f16-browser/download",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
