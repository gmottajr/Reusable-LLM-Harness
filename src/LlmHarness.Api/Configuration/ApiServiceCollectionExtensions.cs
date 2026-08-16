using LlmHarness.Core.Configuration;
using LlmHarness.Core.Harness;
using LlmHarness.Core.Interfaces;
using LlmHarness.ManagedModels.Catalog;
using LlmHarness.ManagedModels.Runtime;
using LlmHarness.ManagedModels.Storage;
using LlmHarness.Providers.Local.Managed;
using LlmHarness.Providers.OpenAI;

namespace LlmHarness.Api.Configuration;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddLlmHarnessApi(this IServiceCollection services)
    {
        var runtimeOptions = new ManagedRuntimeOptions();
        var modelStoragePath = Environment.GetEnvironmentVariable("LLM_HARNESS_MODEL_STORAGE") ??
            Path.Combine(AppContext.BaseDirectory, "managed-models");

        services.AddHttpClient(OpenAiProvider.HttpClientName);
        services.AddHttpClient("LlmHarness.ManagedModelDownload");
        services.AddHttpClient("LlmHarness.ManagedRuntime");
        services.AddHttpClient("LlmHarness.ManagedLocal");
        services.AddSingleton<OpenAiOptions>();
        services.AddSingleton(runtimeOptions);
        services.AddSingleton<IModelCatalogService, ModelCatalogService>();
        services.AddSingleton<IModelStorageService>(
            new ModelStorageService(modelStoragePath));
        services.AddSingleton<IModelDownloadService>(serviceProvider =>
            new ModelDownloadService(
                serviceProvider.GetRequiredService<IModelCatalogService>(),
                serviceProvider.GetRequiredService<IModelStorageService>(),
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LlmHarness.ManagedModelDownload")));
        services.AddSingleton<IModelRuntimeService>(serviceProvider =>
            new ModelRuntimeService(
                serviceProvider.GetRequiredService<ManagedRuntimeOptions>(),
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LlmHarness.ManagedRuntime")));
        services.AddSingleton<ManagedLocalProvider>(serviceProvider =>
            new ManagedLocalProvider(
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LlmHarness.ManagedLocal"),
                serviceProvider.GetRequiredService<IModelCatalogService>(),
                serviceProvider.GetRequiredService<IModelStorageService>(),
                serviceProvider.GetRequiredService<IModelRuntimeService>(),
                new ManagedLocalProviderOptions
                {
                    DefaultModelId = runtimeOptions.DefaultModelId,
                    AutoStart = runtimeOptions.AutoStart
                }));
        services.AddSingleton<ILlmProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ManagedLocalProvider>());
        services.AddSingleton<ILlmProvider>(serviceProvider =>
            new OpenAiProvider(
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<OpenAiOptions>()));
        services.AddSingleton<ILlmHarness>(serviceProvider =>
            new LlmHarness(
                serviceProvider.GetRequiredService<IEnumerable<ILlmProvider>>(),
                timeoutOptions: new LlmTimeoutOptions(),
                providerSelectionOptions: new ProviderSelectionOptions()));

        return services;
    }
}
