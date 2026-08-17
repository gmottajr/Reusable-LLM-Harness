using LlmHarness.Core.Configuration;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Harness;
using LlmHarness.Core.Interfaces;
using LlmHarness.Api.Logging;
using LlmHarness.ManagedModels.Catalog;
using LlmHarness.ManagedModels.Runtime;
using LlmHarness.ManagedModels.Storage;
using LlmHarness.Providers.Local.Managed;
using LlmHarness.Providers.Local.External;
using LlmHarness.Providers.OpenAI;
using CoreLlmHarness = LlmHarness.Core.Harness.LlmHarness;

namespace LlmHarness.Api.Configuration;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddLlmHarnessApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ILlmHarnessLogger, AspNetLlmHarnessLogger>();
        var runtimeOptions = new ManagedRuntimeOptions();
        var modelStoragePath = Environment.GetEnvironmentVariable("LLM_HARNESS_MODEL_STORAGE") ??
            Path.Combine(AppContext.BaseDirectory, "managed-models");

        services.AddHttpClient(OpenAiProvider.HttpClientName);
        services.AddHttpClient("LlmHarness.Mistral");
        services.AddHttpClient("LlmHarness.Grok");
        services.AddHttpClient("LlmHarness.GoogleGemini");
        services.AddHttpClient("LlmHarness.ManagedModelDownload");
        services.AddHttpClient("LlmHarness.ManagedRuntime");
        services.AddHttpClient("LlmHarness.ManagedLocal");
        services.AddHttpClient("LlmHarness.InstalledLocal");
        services.AddSingleton(new OpenAiOptions
        {
            ApiKey = configuration["OPENAI_API_KEY"] ?? string.Empty,
            Endpoint = configuration["OPENAI_ENDPOINT"] ??
                "https://api.openai.com/v1/chat/completions",
            DefaultModel = configuration["OPENAI_DEFAULT_MODEL"] ?? "gpt-4o-mini"
        });
        services.AddSingleton(new GoogleGeminiOptions
        {
            ApiKey = configuration["Google-LLM:ApiKey"] ??
                configuration["GOOGLE_GEMINI_API_KEY"] ?? string.Empty,
            Endpoint = configuration["Google-LLM:URL"] ??
                configuration["GOOGLE_GEMINI_ENDPOINT"] ??
                "https://generativelanguage.googleapis.com/v1beta",
            DefaultModel = configuration["Google-LLM:Model"] ??
                configuration["GOOGLE_GEMINI_MODEL"] ?? "gemini-flash-latest"
        });
        services.AddSingleton(new CompatibleCloudProviderOptions
        {
            Provider = LlmProviderKind.Mistral,
            ApiKey = configuration["Mistral-API-settings:ApiKey"] ??
                configuration["MISTRAL_API_KEY"] ?? string.Empty,
            Endpoint = configuration["Mistral-API-settings:URL"] ??
                configuration["MISTRAL_ENDPOINT"] ??
                "https://api.mistral.ai/v1/chat/completions",
            DefaultModel = configuration["Mistral-API-settings:Model"] ??
                configuration["MISTRAL_MODEL"] ?? "mistral-small-latest"
        });
        services.AddSingleton(new CompatibleCloudProviderOptions
        {
            Provider = LlmProviderKind.Grok,
            ApiKey = configuration["Grok-API-settings:XAI_API_KEY"] ??
                configuration["XAI_API_KEY"] ?? string.Empty,
            Endpoint = configuration["Grok-API-settings:URL"] ??
                configuration["XAI_ENDPOINT"] ??
                "https://api.x.ai/v1/chat/completions",
            DefaultModel = configuration["Grok-API-settings:Model"] ??
                configuration["XAI_MODEL"] ?? "grok-4.5"
        });
        services.AddSingleton(new InstalledLocalProviderOptions(
            configuration["LLM_HARNESS_INSTALLED_LOCAL_ENDPOINT"],
            configuration["LLM_HARNESS_INSTALLED_LOCAL_MODEL"],
            configuration["LLM_HARNESS_INSTALLED_LOCAL_API_KEY"]));
        services.AddSingleton(runtimeOptions);
        services.AddSingleton<IModelCatalogService, ModelCatalogService>();
        services.AddSingleton<IModelStorageService>(
            new ModelStorageService(modelStoragePath));
        services.AddSingleton<IModelDownloadService>(serviceProvider =>
            new ModelDownloadService(
                serviceProvider.GetRequiredService<IModelCatalogService>(),
                serviceProvider.GetRequiredService<IModelStorageService>(),
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LlmHarness.ManagedModelDownload"),
                message => serviceProvider
                    .GetRequiredService<ILogger<ModelDownloadService>>()
                    .LogInformation("{ManagedModelDownloadEvent}", message)));
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
        services.AddSingleton<InstalledLocalProvider>(serviceProvider =>
            new InstalledLocalProvider(
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LlmHarness.InstalledLocal"),
                serviceProvider.GetRequiredService<InstalledLocalProviderOptions>()));
        services.AddSingleton<ILlmProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<InstalledLocalProvider>());
        services.AddSingleton<ILlmProvider>(serviceProvider =>
            new OpenAiProvider(
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<OpenAiOptions>()));
        services.AddSingleton<GoogleGeminiProvider>(serviceProvider =>
            new GoogleGeminiProvider(
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LlmHarness.GoogleGemini"),
                serviceProvider.GetRequiredService<GoogleGeminiOptions>()));
        services.AddSingleton<ILlmProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GoogleGeminiProvider>());
        services.AddSingleton<CompatibleCloudProvider>(serviceProvider =>
            new CompatibleCloudProvider(
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LlmHarness.Mistral"),
                serviceProvider
                    .GetServices<CompatibleCloudProviderOptions>()
                    .Single(options => options.Provider == LlmProviderKind.Mistral)));
        services.AddSingleton<ILlmProvider>(serviceProvider =>
            serviceProvider.GetServices<CompatibleCloudProvider>()
                .Single(provider => provider.Kind == LlmProviderKind.Mistral));
        services.AddSingleton<CompatibleCloudProvider>(serviceProvider =>
            new CompatibleCloudProvider(
                serviceProvider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LlmHarness.Grok"),
                serviceProvider
                    .GetServices<CompatibleCloudProviderOptions>()
                    .Single(options => options.Provider == LlmProviderKind.Grok)));
        services.AddSingleton<ILlmProvider>(serviceProvider =>
            serviceProvider.GetServices<CompatibleCloudProvider>()
                .Single(provider => provider.Kind == LlmProviderKind.Grok));
        services.AddSingleton<ILlmHarness>(serviceProvider =>
            new CoreLlmHarness(
                serviceProvider.GetRequiredService<IEnumerable<ILlmProvider>>(),
                timeoutOptions: new LlmTimeoutOptions(),
                providerSelectionOptions: new ProviderSelectionOptions(),
                logger: serviceProvider.GetRequiredService<ILlmHarnessLogger>()));

        return services;
    }
}
