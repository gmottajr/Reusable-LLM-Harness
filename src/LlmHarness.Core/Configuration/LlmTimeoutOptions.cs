namespace LlmHarness.Core.Configuration;

public sealed record LlmTimeoutOptions
{
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (DefaultTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultTimeout),
                "Default timeout must be positive.");
        }
    }
}
