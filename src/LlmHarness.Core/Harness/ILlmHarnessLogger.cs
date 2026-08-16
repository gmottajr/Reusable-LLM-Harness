namespace LlmHarness.Core.Harness;

public interface ILlmHarnessLogger
{
    void Log(LlmLogEvent logEvent);
}
