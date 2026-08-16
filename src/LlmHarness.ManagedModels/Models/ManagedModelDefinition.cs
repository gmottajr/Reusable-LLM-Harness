namespace LlmHarness.ManagedModels.Models;

public sealed record ManagedModelDefinition(
    string Id,
    string Name,
    string Creator,
    string Description,
    Uri DownloadUri,
    string FileName,
    long? SizeBytes,
    string Sha256,
    string RuntimeModelName,
    string License);
