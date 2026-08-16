namespace LlmHarness.ManagedModels.Models;

public enum ManagedModelState
{
    NotDownloaded,
    Downloading,
    Downloaded,
    Starting,
    Running,
    Failed
}

public sealed record ModelDownloadProgress(
    long BytesDownloaded,
    long? TotalBytes,
    double Percentage);

public sealed record ManagedModelStatus(
    string ModelId,
    ManagedModelState State,
    long BytesDownloaded = 0,
    long? TotalBytes = null,
    double Percentage = 0,
    string? Error = null,
    DateTimeOffset? UpdatedAt = null);

public sealed record ModelStorageStatus(
    bool IsPresent,
    bool IsValid,
    long BytesDownloaded,
    string? Error = null);

public enum ManagedRuntimeState
{
    Stopped,
    Starting,
    Running,
    Failed
}

public sealed record ManagedRuntimeStatus(
    string? ModelId,
    ManagedRuntimeState State,
    Uri BaseUri,
    string? Error = null);
