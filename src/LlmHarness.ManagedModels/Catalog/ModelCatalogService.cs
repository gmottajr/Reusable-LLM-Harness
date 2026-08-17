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
            "Apache-2.0",
            "SmolLM2-135M-Instruct-q0f32-MLC",
            false,
            719.38,
            "lightweight",
            true),
        new ManagedModelDefinition(
            "qwen2.5-0.5b-instruct-q4km",
            "Qwen2.5 0.5B Instruct",
            "Qwen / Hugging Face",
            "A small Apache-licensed instruction model for quick local experiments.",
            new Uri("https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf?download=true"),
            "qwen2.5-0.5b-instruct-q4_k_m.gguf",
            491400032,
            "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db",
            "qwen2.5-0.5b-instruct",
            "Apache-2.0"),
        new ManagedModelDefinition(
            "qwen3-0.6b-q4f16-browser",
            "Qwen Small 0.6B Instruct",
            "Qwen / MLC / Hugging Face",
            "A browser-only Qwen3 0.6B q4f16 model for lightweight local inference.",
            new Uri("https://huggingface.co/mlc-ai/Qwen3-0.6B-q4f16_1-MLC"),
            "qwen3-0.6b-q4f16-browser",
            null,
            "browser-only",
            "qwen3-0.6b-q4f16-browser",
            "Apache-2.0",
            "Qwen3-0.6B-q4f16_1-MLC",
            true,
            1403.34,
            "lightweight",
            true),
        new ManagedModelDefinition(
            "qwen2.5-0.5b-instruct-q4f16-browser",
            "Qwen Tiny 0.5B Instruct",
            "Qwen / Hugging Face",
            "A low-memory browser model using the WebLLM q4f16 artifact. This entry is browser-only.",
            new Uri("https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf?download=true"),
            "qwen2.5-0.5b-instruct-q4f16-browser.gguf",
            491400032,
            "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db",
            "qwen2.5-0.5b-instruct-q4f16-browser",
            "Apache-2.0",
            "Qwen2.5-0.5B-Instruct-q4f16_1-MLC",
            true,
            944.62,
            "lightweight",
            true),
        new ManagedModelDefinition(
            "deepseek-r1-distill-qwen-1.5b-q4km",
            "DeepSeek-R1 Distill Qwen 1.5B",
            "Unsloth / Hugging Face",
            "A compact reasoning model distilled into the Qwen 1.5B architecture.",
            new Uri("https://huggingface.co/unsloth/DeepSeek-R1-Distill-Qwen-1.5B-GGUF/resolve/main/DeepSeek-R1-Distill-Qwen-1.5B-Q4_K_M.gguf?download=true"),
            "DeepSeek-R1-Distill-Qwen-1.5B-Q4_K_M.gguf",
            1117321312,
            "f3bdf9cf31dee4b57ae4e455a1cb0d01b5c2c1b50d72d3112141c195506c2840",
            "deepseek-r1-distill-qwen-1.5b",
            "Apache-2.0"),
        new ManagedModelDefinition(
            "deepseek-r1-distill-qwen-7b-q4f16-browser",
            "DeepSeek-R1 Distill Qwen 7B",
            "MLC / Hugging Face",
            "A browser-only 7B q4f16 reasoning model. This entry is intentionally marked heavy.",
            new Uri("https://huggingface.co/mlc-ai/DeepSeek-R1-Distill-Qwen-7B-q4f16_1-MLC"),
            "deepseek-r1-distill-qwen-7b-q4f16-browser",
            null,
            "browser-only",
            "deepseek-r1-distill-qwen-7b-q4f16-browser",
            "Apache-2.0",
            "DeepSeek-R1-Distill-Qwen-7B-q4f16_1-MLC",
            true,
            5106.67,
            "heavy",
            false,
            "This model may require around 5–6 GB of GPU memory and may take several minutes to load or respond in the browser. Recommended only for capable desktop machines."),
        new ManagedModelDefinition(
            "gemma-3-1b-it-q4km",
            "Gemma 3 1B Instruct",
            "Google / ggml-org",
            "Google's compact instruction model packaged as a llama.cpp-compatible GGUF.",
            new Uri("https://huggingface.co/ggml-org/gemma-3-1b-it-GGUF/resolve/main/gemma-3-1b-it-Q4_K_M.gguf?download=true"),
            "gemma-3-1b-it-Q4_K_M.gguf",
            806058240,
            "8ccc5cd1f1b3602548715ae25a66ed73fd5dc68a210412eea643eb20eb75a135",
            "gemma-3-1b-it",
            "Gemma Terms of Use",
            "gemma3-1b-it-q4f16_1-MLC",
            false,
            711.07,
            "standard",
            true)
    ];

    public IReadOnlyList<ManagedModelDefinition> GetAll() => Models;

    public ManagedModelDefinition? Find(string modelId) =>
        Models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
}
