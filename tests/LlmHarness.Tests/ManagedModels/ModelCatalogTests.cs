using LlmHarness.ManagedModels.Catalog;

namespace LlmHarness.Tests.ManagedModels;

public sealed class ModelCatalogTests
{
    [Fact]
    public void Catalog_contains_curated_models_only()
    {
        var catalog = new ModelCatalogService();
        var models = catalog.GetAll();

        Assert.NotEmpty(models);
        Assert.All(models, model =>
        {
            Assert.Equal("huggingface.co", model.DownloadUri.Host);
            Assert.False(string.IsNullOrWhiteSpace(model.Sha256));
            Assert.DoesNotContain("..", model.FileName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Unknown_model_id_is_not_resolvable()
    {
        var catalog = new ModelCatalogService();

        Assert.Null(catalog.Find("https://example.test/arbitrary-model"));
    }
}
