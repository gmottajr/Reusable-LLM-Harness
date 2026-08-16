using LlmHarness.ManagedModels.Models;

namespace LlmHarness.ManagedModels.Runtime;

public interface IModelRuntimeService
{
    Task<ManagedRuntimeStatus> StartAsync(
        ManagedModelDefinition model,
        string modelPath,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<ManagedRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Uri GetCompletionUri();
}
