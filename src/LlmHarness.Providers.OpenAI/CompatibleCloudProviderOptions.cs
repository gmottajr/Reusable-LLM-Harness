using LlmHarness.Core.Enums;

namespace LlmHarness.Providers.OpenAI;

public sealed record CompatibleCloudProviderOptions
{
    public required LlmProviderKind Provider { get; init; }

    public string ApiKey { get; init; } = string.Empty;

    public required string Endpoint { get; init; }

    public required string DefaultModel { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string? AvailabilityReason => IsConfigured
        ? null
        : $"Missing {Provider} API key.";

    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                $"{Provider} endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(Endpoint));
        }

        if (string.IsNullOrWhiteSpace(DefaultModel))
        {
            throw new ArgumentException(
                $"{Provider} default model cannot be empty.",
                nameof(DefaultModel));
        }
    }
}
