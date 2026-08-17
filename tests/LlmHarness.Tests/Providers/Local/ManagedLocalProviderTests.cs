using System.Net;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.ManagedModels.Catalog;
using LlmHarness.ManagedModels.Models;
using LlmHarness.ManagedModels.Runtime;
using LlmHarness.ManagedModels.Storage;
using LlmHarness.Providers.Local.Managed;

namespace LlmHarness.Tests.Providers.Local;

public sealed class ManagedLocalProviderTests
{
    [Fact]
    public async Task Provider_maps_openai_compatible_runtime_response()
    {
        var model = new ModelCatalogService().GetAll()[0];
        var handler = new FixtureHandler();
        using var client = new HttpClient(handler);
        var provider = new ManagedLocalProvider(
            client,
            new FixtureCatalog(model),
            new FixtureStorage(),
            new FixtureRuntime(model),
            new ManagedLocalProviderOptions { DefaultModelId = model.Id });

        var result = await provider.CompleteAsync(
            new LlmProviderRequest(
                [new(LlmMessageRole.User, "Hello")],
                LlmProviderKind.LocalOpenAiCompatible,
                model.Id,
                temperature: 0.2,
                maxTokens: 32));

        Assert.Equal("local response", result.Content);
        Assert.Equal(LlmProviderKind.LocalOpenAiCompatible, result.Provider);
        Assert.Equal(1, handler.Requests);
    }

    private sealed class FixtureCatalog(ManagedModelDefinition model) : IModelCatalogService
    {
        public IReadOnlyList<ManagedModelDefinition> GetAll() => [model];

        public ManagedModelDefinition? Find(string modelId) =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase) ? model : null;
    }

    private sealed class FixtureStorage : IModelStorageService
    {
        public string GetModelPath(ManagedModelDefinition value) => "/tmp/fixture.gguf";

        public Task<ModelStorageStatus> InspectAsync(
            ManagedModelDefinition value,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelStorageStatus(true, true, value.SizeBytes ?? 1));

        public Task SaveAsync(
            ManagedModelDefinition value,
            Stream content,
            long? totalBytes,
            IProgress<ModelDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixtureRuntime(ManagedModelDefinition model) : IModelRuntimeService
    {
        public Task<ManagedRuntimeStatus> StartAsync(
            ManagedModelDefinition value,
            string modelPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ManagedRuntimeStatus(model.Id, ManagedRuntimeState.Running, new Uri("http://runtime.test")));

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ManagedRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ManagedRuntimeStatus(model.Id, ManagedRuntimeState.Running, new Uri("http://runtime.test")));

        public Uri GetCompletionUri() => new("http://runtime.test/v1/chat/completions");
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"id\":\"local-1\",\"choices\":[{\"message\":{\"content\":\"local response\"},\"finish_reason\":\"stop\"}]}" )
            });
        }
    }
}
