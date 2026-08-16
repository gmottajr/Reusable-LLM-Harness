using LlmHarness.Core.Contracts;

namespace LlmHarness.Core.Validation;

public sealed class LlmValidationResult
{
    private LlmValidationResult(IReadOnlyList<LlmError> errors)
    {
        Errors = errors;
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<LlmError> Errors { get; }

    public static LlmValidationResult Valid() =>
        new(Array.Empty<LlmError>());

    public static LlmValidationResult Invalid(IEnumerable<LlmError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var materializedErrors = errors.ToArray();
        if (materializedErrors.Length == 0)
        {
            throw new ArgumentException("At least one validation error is required.", nameof(errors));
        }

        return new(materializedErrors);
    }
}
