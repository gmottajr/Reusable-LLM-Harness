namespace LlmHarness.Providers.Local.External;

public sealed class InstalledLocalProviderOptions
{
    public InstalledLocalProviderOptions(
        string? endpoint = null,
        string? model = null,
        string? apiKey = null)
    {
        Endpoint = endpoint ?? Environment.GetEnvironmentVariable("LLM_HARNESS_INSTALLED_LOCAL_ENDPOINT") ??
            "http://127.0.0.1:11434/v1";
        Model = model ?? Environment.GetEnvironmentVariable("LLM_HARNESS_INSTALLED_LOCAL_MODEL") ??
            "llama3.2";
        ApiKey = apiKey ?? Environment.GetEnvironmentVariable("LLM_HARNESS_INSTALLED_LOCAL_API_KEY");
    }

    public string Endpoint { get; private set; }

    public string Model { get; private set; }

    public string? ApiKey { get; }

    public bool IsConfigured => TryValidate(out _);

    public void Update(string endpoint, string model)
    {
        Endpoint = NormalizeEndpoint(endpoint);
        Model = model.Trim();
        Validate();
    }

    public bool TryValidate(out string? error)
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            error = "The installed local endpoint must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            error = "An installed local model name is required.";
            return false;
        }

        error = null;
        return true;
    }

    public void Validate()
    {
        if (!TryValidate(out var error))
        {
            throw new ArgumentException(error);
        }
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        const string completionsSuffix = "/chat/completions";
        return normalized.EndsWith(completionsSuffix, StringComparison.OrdinalIgnoreCase)
            ? normalized[..^completionsSuffix.Length]
            : normalized;
    }
}
