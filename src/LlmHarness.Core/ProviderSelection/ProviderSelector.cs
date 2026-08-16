using LlmHarness.Core.Configuration;
using LlmHarness.Core.Contracts;
using LlmHarness.Core.Enums;
using LlmHarness.Core.Interfaces;

namespace LlmHarness.Core.ProviderSelection;

public sealed class ProviderSelector : IProviderSelector
{
    private readonly IReadOnlyList<ILlmProvider> _providers;
    private readonly ProviderSelectionOptions _options;

    public ProviderSelector(
        IEnumerable<ILlmProvider> providers,
        ProviderSelectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToArray();
        _options = options ?? new ProviderSelectionOptions();
        _options.Validate();
    }

    public async Task<ProviderSelectionResult> SelectAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var candidates = GetCandidates(request).ToArray();
        if (!request.Provider.HasValue && EffectiveMode(request) == LlmExecutionMode.Manual)
        {
            return ProviderSelectionResult.Failed(
                new LlmError(
                    LlmErrorType.ProviderUnavailable,
                    "Manual provider selection requires a provider.",
                    Retryable: false,
                    Code: "provider"));
        }

        if (candidates.Length == 0)
        {
            return ProviderSelectionResult.Failed(
                new LlmError(
                    LlmErrorType.ProviderUnavailable,
                    "No configured LLM provider is available.",
                    Retryable: false,
                    Code: request.Provider?.ToString() ?? "provider"));
        }

        foreach (var provider in candidates)
        {
            bool available;
            try
            {
                available = await provider.IsAvailableAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                available = false;
            }

            if (available)
            {
                return ProviderSelectionResult.Found(provider);
            }
        }

        return ProviderSelectionResult.Failed(
            new LlmError(
                LlmErrorType.ProviderUnavailable,
                "No configured LLM provider is available.",
                Retryable: false,
                Code: request.Provider?.ToString() ?? "provider"));
    }

    private IEnumerable<ILlmProvider> GetCandidates(LlmRequest request)
    {
        if (request.Provider is { } requestedProvider)
        {
            return _providers.Where(provider => provider.Kind == requestedProvider);
        }

        var preference = EffectiveMode(request) == LlmExecutionMode.AutoPreferLocal
            ? _options.LocalPreference
            : _options.CloudPreference;

        return _providers
            .OrderBy(provider => IndexOf(preference, provider.Kind));
    }

    private LlmExecutionMode EffectiveMode(LlmRequest request) =>
        request.HasExecutionModeOverride
            ? request.ExecutionMode
            : _options.DefaultExecutionMode;

    private static int IndexOf(
        IReadOnlyList<LlmProviderKind> preference,
        LlmProviderKind kind)
    {
        for (var index = 0; index < preference.Count; index++)
        {
            if (preference[index] == kind)
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
