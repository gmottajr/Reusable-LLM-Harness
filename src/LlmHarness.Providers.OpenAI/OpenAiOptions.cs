namespace LlmHarness.Providers.OpenAI;

public sealed record OpenAiOptions
{
    public string ApiKey { get; init; } =
        Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;

    public string Endpoint { get; init; } =
        Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ??
        "https://api.openai.com/v1/chat/completions";

    public string DefaultModel { get; init; } =
        Environment.GetEnvironmentVariable("OPENAI_DEFAULT_MODEL") ?? "gpt-4o-mini";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string? AvailabilityReason => IsConfigured
        ? null
        : "Missing OPENAI_API_KEY environment variable.";

    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("OpenAI endpoint must be an absolute HTTP or HTTPS URI.", nameof(Endpoint));
        }

        if (string.IsNullOrWhiteSpace(DefaultModel))
        {
            throw new ArgumentException("OpenAI default model cannot be empty.", nameof(DefaultModel));
        }
    }
}
