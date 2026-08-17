using LlmHarness.Core.Harness;

namespace LlmHarness.Tests.Harness;

public sealed class LlmPayloadRedactorTests
{
    [Fact]
    public void Redacts_credentials_in_non_json_text()
    {
        var redacted = LlmPayloadRedactor.Redact("Authorization: Bearer token-value; api_key=secret-value")!;

        Assert.DoesNotContain("token-value", redacted);
        Assert.DoesNotContain("secret-value", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }
}
