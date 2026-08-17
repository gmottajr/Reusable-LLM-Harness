namespace LlmHarness.Api.Models;

public sealed record LlmSourceStatusResponse(
    string Id,
    string Name,
    string Description,
    string Provider,
    bool Configured,
    bool Available,
    string? Reason,
    string? Endpoint = null,
    string? Model = null);

public sealed record InstalledLocalSetupResponse(
    string Endpoint,
    string Model,
    bool Configured,
    bool Available,
    string? Reason);

public sealed record ConfigureInstalledLocalRequest(
    string? Endpoint,
    string? Model);
