using LlmHarness.ManagedModels.Models;

namespace LlmHarness.ManagedModels.Catalog;

public interface IModelCatalogService
{
    IReadOnlyList<ManagedModelDefinition> GetAll();

    ManagedModelDefinition? Find(string modelId);
}
