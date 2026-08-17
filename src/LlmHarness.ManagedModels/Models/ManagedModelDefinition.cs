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
    string License,
    string? BrowserModelId = null,
    bool BrowserOnly = false,
    double? BrowserVramRequiredMb = null,
    string? BrowserTier = null,
    bool BrowserRecommended = false,
    string? BrowserWarning = null);
