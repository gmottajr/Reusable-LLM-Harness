namespace LlmHarness.Core.Schema;

public interface ISchemaValidator
{
    SchemaValidationResult Validate(string? json, string? schema);
}
