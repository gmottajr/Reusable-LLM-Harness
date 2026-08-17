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

    [Fact]
    public void Catalog_includes_small_browser_models()
    {
        var catalog = new ModelCatalogService();

        Assert.Contains(catalog.GetAll(), model => model.Id == "qwen2.5-0.5b-instruct-q4km");
        Assert.Contains(catalog.GetAll(), model =>
            model.Id == "qwen3-0.6b-q4f16-browser" &&
            model.BrowserModelId == "Qwen3-0.6B-q4f16_1-MLC" &&
            model.BrowserOnly &&
            model.BrowserTier == "lightweight");
        Assert.Contains(catalog.GetAll(), model =>
            model.BrowserModelId == "Qwen2.5-0.5B-Instruct-q4f16_1-MLC" &&
            model.BrowserOnly &&
            model.BrowserVramRequiredMb < 1000);
        Assert.Contains(catalog.GetAll(), model => model.Id == "deepseek-r1-distill-qwen-1.5b-q4km");
        Assert.Contains(catalog.GetAll(), model =>
            model.Id == "deepseek-r1-distill-qwen-7b-q4f16-browser" &&
            model.BrowserModelId == "DeepSeek-R1-Distill-Qwen-7B-q4f16_1-MLC" &&
            model.BrowserOnly &&
            model.BrowserTier == "heavy" &&
            !model.BrowserRecommended &&
            model.BrowserWarning is not null);
        Assert.Contains(catalog.GetAll(), model => model.Id == "gemma-3-1b-it-q4km");
    }
}
