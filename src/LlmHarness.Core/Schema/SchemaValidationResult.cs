namespace LlmHarness.Core.Schema;

public sealed class SchemaValidationResult
{
    private SchemaValidationResult(IReadOnlyList<SchemaValidationError> errors)
    {
        Errors = errors;
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<SchemaValidationError> Errors { get; }

    public static SchemaValidationResult Valid() =>
        new(Array.Empty<SchemaValidationError>());

    public static SchemaValidationResult Invalid(IEnumerable<SchemaValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var materializedErrors = errors.ToArray();
        if (materializedErrors.Length == 0)
        {
            throw new ArgumentException("At least one schema validation error is required.", nameof(errors));
        }

        return new(materializedErrors);
    }
}
