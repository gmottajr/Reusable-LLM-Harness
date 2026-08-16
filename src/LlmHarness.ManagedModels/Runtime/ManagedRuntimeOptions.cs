namespace LlmHarness.ManagedModels.Runtime;

public sealed record ManagedRuntimeOptions
{
    public string ExecutablePath { get; init; } =
        Environment.GetEnvironmentVariable("LLM_HARNESS_RUNTIME_EXECUTABLE") ?? "llama-server";

    public Uri BaseUri { get; init; } = new("http://127.0.0.1:8081");

    public int Port { get; init; } = 8081;

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public string DefaultModelId { get; init; } =
        Environment.GetEnvironmentVariable("LLM_HARNESS_MANAGED_MODEL_ID") ??
        "smollm2-135m-instruct-q4km";

    public bool AutoStart { get; init; } =
        bool.TryParse(
            Environment.GetEnvironmentVariable("LLM_HARNESS_RUNTIME_AUTO_START"),
            out var autoStart) && autoStart;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            throw new ArgumentException("Managed runtime executable path is required.", nameof(ExecutablePath));
        }

        if (!BaseUri.IsAbsoluteUri || BaseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Managed runtime base URI must be an absolute HTTP or HTTPS URI.", nameof(BaseUri));
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port));
        }

        if (StartupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StartupTimeout));
        }

        if (string.IsNullOrWhiteSpace(DefaultModelId))
        {
            throw new ArgumentException("A default managed model ID is required.", nameof(DefaultModelId));
        }
    }
}
