using LlmHarness.ManagedModels.Models;

namespace LlmHarness.ManagedModels.Catalog;

public sealed class ModelCatalogService : IModelCatalogService
{
    private static readonly IReadOnlyList<ManagedModelDefinition> Models =
    [
        new ManagedModelDefinition(
            "smollm2-135m-instruct-q4km",
            "SmolLM2 135M Instruct",
            "Mungert / Hugging Face",
            "A compact instruction model suitable for local playground use.",
            new Uri("https://huggingface.co/Mungert/SmolLM2-135M-Instruct-GGUF/resolve/main/SmolLM2-135M-Instruct-q4_k_m.gguf?download=true"),
            "SmolLM2-135M-Instruct-q4_k_m.gguf",
            null,
            "4bd46e022f32b681ae1beba1030d9ad042a655e75239fe83267f5e3bba98801d",
            "smollm2-135m-instruct",
            "Apache-2.0")
    ];

    public IReadOnlyList<ManagedModelDefinition> GetAll() => Models;

    public ManagedModelDefinition? Find(string modelId) =>
        Models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
}
