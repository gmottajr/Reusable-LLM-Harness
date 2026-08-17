using LlmHarness.Api.Configuration;
using LlmHarness.Providers.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmHarness.Tests.Api;

public sealed class ApiConfigurationTests
{
    [Fact]
    public void Reads_openai_user_secret_section_into_injected_options_dto()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI-API-settings:ApiKey"] = "nested-key",
                ["OpenAI-API-settings:URL"] = "https://example.test/chat",
                ["OpenAI-API-settings:Model"] = "nested-model"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLlmHarnessApi(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<OpenAiOptions>();

        Assert.Equal("nested-key", options.ApiKey);
        Assert.Equal("https://example.test/chat", options.Endpoint);
        Assert.Equal("nested-model", options.DefaultModel);
    }
}
