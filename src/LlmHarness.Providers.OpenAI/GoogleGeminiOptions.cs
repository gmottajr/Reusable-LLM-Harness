namespace LlmHarness.Providers.OpenAI;

public sealed record GoogleGeminiOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public string Endpoint { get; init; } =
        "https://generativelanguage.googleapis.com/v1beta";

    public string DefaultModel { get; init; } = "gemini-flash-latest";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string? AvailabilityReason => IsConfigured
        ? null
        : "Missing Google Gemini API key.";

    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Google Gemini endpoint must be an absolute HTTP or HTTPS URI.",
                nameof(Endpoint));
        }

        if (string.IsNullOrWhiteSpace(DefaultModel))
        {
            throw new ArgumentException(
                "Google Gemini default model cannot be empty.",
                nameof(DefaultModel));
        }
    }
}
