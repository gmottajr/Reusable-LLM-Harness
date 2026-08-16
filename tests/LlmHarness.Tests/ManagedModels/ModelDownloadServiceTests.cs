using System.Net;
using System.Security.Cryptography;
using System.Text;
using LlmHarness.ManagedModels.Catalog;
using LlmHarness.ManagedModels.Models;
using LlmHarness.ManagedModels.Storage;

namespace LlmHarness.Tests.ManagedModels;

public sealed class ModelDownloadServiceTests
{
    [Fact]
    public async Task Download_reports_verified_completion_for_catalog_model()
    {
        var content = Encoding.UTF8.GetBytes("download fixture");
        var model = CreateModel(content);
        var root = Path.Combine(Path.GetTempPath(), $"llm-harness-download-test-{Guid.NewGuid():N}");

        try
        {
            using var client = new HttpClient(new FixtureHandler(content));
            var service = new ModelDownloadService(
                new FixtureCatalog(model),
                new ModelStorageService(root),
                client);

            var status = await service.DownloadAsync(model.Id);

            Assert.Equal(ManagedModelState.Downloaded, status.State);
            Assert.Equal(100, status.Percentage);
            Assert.Equal((long)content.Length, status.BytesDownloaded);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Download_failure_is_available_as_status_without_arbitrary_url_input()
    {
        var model = CreateModel(Encoding.UTF8.GetBytes("download fixture"));
        var root = Path.Combine(Path.GetTempPath(), $"llm-harness-download-test-{Guid.NewGuid():N}");

        try
        {
            using var client = new HttpClient(new FixtureHandler(Array.Empty<byte>(), HttpStatusCode.NotFound));
            var service = new ModelDownloadService(
                new FixtureCatalog(model),
                new ModelStorageService(root),
                client);

            var status = await service.DownloadAsync(model.Id);

            Assert.Equal(ManagedModelState.Failed, status.State);
            Assert.Contains("404", status.Error!);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ManagedModelDefinition CreateModel(byte[] content) =>
        new(
            "fixture-model",
            "Fixture",
            "Tests",
            "Test model",
            new Uri("https://catalog.test/fixture.gguf"),
            "fixture.gguf",
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            "fixture",
            "Test");

    private sealed class FixtureCatalog(ManagedModelDefinition model) : IModelCatalogService
    {
        public IReadOnlyList<ManagedModelDefinition> GetAll() => [model];

        public ManagedModelDefinition? Find(string modelId) =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase) ? model : null;
    }

    private sealed class FixtureHandler(byte[] content, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(content)
            });
    }
}
