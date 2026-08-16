using LlmHarness.Core.Contracts;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Core.Validation;

public sealed class ValidatingLlmHarness : ILlmHarness
{
    private readonly ILlmHarness _innerHarness;
    private readonly ILlmRequestValidator _validator;

    public ValidatingLlmHarness(
        ILlmHarness innerHarness,
        ILlmRequestValidator validator)
    {
        _innerHarness = innerHarness ?? throw new ArgumentNullException(nameof(innerHarness));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public Task<LlmResult<TOutput>> ExecuteAsync<TOutput>(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(request);
        if (!validation.IsValid)
        {
            return Task.FromResult(
                LlmResult<TOutput>.CreateFailure(validation.Errors[0]));
        }

        return _innerHarness.ExecuteAsync<TOutput>(request, cancellationToken);
    }
}
