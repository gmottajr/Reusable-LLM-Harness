namespace LlmHarness.Core.Harness;

public sealed class NullLlmHarnessLogger : ILlmHarnessLogger
{
    public void Log(LlmLogEvent logEvent)
    {
    }
}
