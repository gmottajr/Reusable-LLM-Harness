namespace LlmHarness.Core.Exceptions;

public sealed class LlmProviderException : Exception
{
    public LlmProviderException(
        string message,
        int? statusCode = null,
        string? providerCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int? StatusCode { get; }

    public string? ProviderCode { get; }
}
