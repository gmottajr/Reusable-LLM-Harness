using LlmHarness.ManagedModels.Models;

namespace LlmHarness.ManagedModels.Storage;

public interface IModelDownloadService
{
    Task<ManagedModelStatus> DownloadAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    Task<ManagedModelStatus> GetStatusAsync(
        string modelId,
        CancellationToken cancellationToken = default);
}
