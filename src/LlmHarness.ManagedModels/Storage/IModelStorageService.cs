using LlmHarness.ManagedModels.Models;

namespace LlmHarness.ManagedModels.Storage;

public interface IModelStorageService
{
    string GetModelPath(ManagedModelDefinition model);

    Task<ModelStorageStatus> InspectAsync(
        ManagedModelDefinition model,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ManagedModelDefinition model,
        Stream content,
        long? totalBytes,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
