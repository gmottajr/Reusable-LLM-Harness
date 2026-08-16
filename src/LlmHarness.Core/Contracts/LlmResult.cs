namespace LlmHarness.Core.Contracts;

public sealed record LlmResult<TOutput>
{
    private LlmResult(
        bool success,
        TOutput? output,
        LlmError? error,
        LlmMetadata metadata)
    {
        Success = success;
        Output = output;
        Error = error;
        Metadata = metadata;
    }

    public bool Success { get; }

    public TOutput? Output { get; }

    public LlmError? Error { get; }

    public LlmMetadata Metadata { get; }

    public static LlmResult<TOutput> CreateSuccess(TOutput output, LlmMetadata? metadata = null) =>
        new(true, output, null, metadata ?? new LlmMetadata());

    public static LlmResult<TOutput> CreateFailure(LlmError error, LlmMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error, metadata ?? new LlmMetadata());
    }
}
