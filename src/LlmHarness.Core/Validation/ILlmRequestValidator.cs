using LlmHarness.Core.Contracts;

namespace LlmHarness.Core.Validation;

public interface ILlmRequestValidator
{
    LlmValidationResult Validate(LlmRequest? request);
}
