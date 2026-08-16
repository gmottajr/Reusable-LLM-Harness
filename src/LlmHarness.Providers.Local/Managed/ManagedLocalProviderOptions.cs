namespace LlmHarness.Providers.Local.Managed;

public sealed record ManagedLocalProviderOptions
{
    public string DefaultModelId { get; init; } = "smollm2-135m-instruct-q4km";

    public bool AutoStart { get; init; }
}
