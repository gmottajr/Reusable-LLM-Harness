using LlmHarness.Core.Enums;

namespace LlmHarness.Core.Contracts;

public sealed record LlmMessage
{
    public LlmMessage(LlmMessageRole role, string content)
    {
        Role = role;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public LlmMessageRole Role { get; }

    public string Content { get; }
}
