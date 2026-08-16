namespace LlmHarness.Core.Schema;

public sealed record SchemaValidationError(string Path, string Message);
