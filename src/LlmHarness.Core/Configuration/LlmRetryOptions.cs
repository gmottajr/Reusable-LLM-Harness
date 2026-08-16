namespace LlmHarness.Core.Configuration;

public sealed record LlmRetryOptions
{
    public int MaxRetries { get; init; } = 3;

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    public bool UseJitter { get; init; } = true;

    public void Validate()
    {
        if (MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), "Max retries cannot be negative.");
        }

        if (InitialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialDelay), "Initial delay cannot be negative.");
        }

        if (MaxDelay < InitialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDelay), "Max delay cannot be less than the initial delay.");
        }
    }
}
