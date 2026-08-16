using LlmHarness.Core.Schema;

namespace LlmHarness.Tests.Schema;

public sealed class JsonSchemaValidatorTests
{
    private readonly JsonSchemaValidator _validator = new();

    [Fact]
    public void Valid_json_matches_object_schema()
    {
        var result = _validator.Validate(
            "{\"name\":\"Ada\",\"age\":37}",
            """
            {
              "type": "object",
              "required": ["name", "age"],
              "properties": {
                "name": { "type": "string" },
                "age": { "type": "integer" }
              }
            }
            """);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Malformed_json_is_rejected()
    {
        var result = _validator.Validate(
            "{\"name\":",
            "{\"type\":\"object\"}");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Message.Contains("invalid JSON"));
    }

    [Fact]
    public void Missing_required_field_is_rejected()
    {
        var result = _validator.Validate(
            "{\"name\":\"Ada\"}",
            """
            {
              "type": "object",
              "required": ["name", "age"],
              "properties": {
                "name": { "type": "string" },
                "age": { "type": "integer" }
              }
            }
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Path == "$.age");
    }

    [Fact]
    public void Wrong_field_type_is_rejected()
    {
        var result = _validator.Validate(
            "{\"age\":\"thirty-seven\"}",
            """
            {
              "type": "object",
              "properties": {
                "age": { "type": "integer" }
              }
            }
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Path == "$.age");
    }

    [Fact]
    public void No_schema_is_an_invalid_validation_request()
    {
        var result = _validator.Validate("{\"answer\":true}", null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Path == "$schema");
    }
}
