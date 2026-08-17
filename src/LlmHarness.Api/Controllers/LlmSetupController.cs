using LlmHarness.Api.Models;
using LlmHarness.Core.Interfaces;
using LlmHarness.Providers.Local.External;
using LlmHarness.Providers.OpenAI;
using Microsoft.AspNetCore.Mvc;

namespace LlmHarness.Api.Controllers;

[ApiController]
[Route("api/setup")]
public sealed class LlmSetupController : ControllerBase
{
    private readonly IEnumerable<ILlmProvider> _providers;
    private readonly OpenAiOptions _openAiOptions;
    private readonly GoogleGeminiOptions _googleGeminiOptions;
    private readonly IReadOnlyList<CompatibleCloudProviderOptions> _compatibleCloudOptions;
    private readonly InstalledLocalProvider _installedLocalProvider;
    private readonly InstalledLocalProviderOptions _installedLocalOptions;

    public LlmSetupController(
        IEnumerable<ILlmProvider> providers,
        OpenAiOptions openAiOptions,
        GoogleGeminiOptions googleGeminiOptions,
        IEnumerable<CompatibleCloudProviderOptions> compatibleCloudOptions,
        InstalledLocalProvider installedLocalProvider,
        InstalledLocalProviderOptions installedLocalOptions)
    {
        _providers = providers;
        _openAiOptions = openAiOptions;
        _googleGeminiOptions = googleGeminiOptions;
        _compatibleCloudOptions = compatibleCloudOptions.ToArray();
        _installedLocalProvider = installedLocalProvider;
        _installedLocalOptions = installedLocalOptions;
    }

    [HttpGet("sources")]
    public async Task<ActionResult<IReadOnlyList<LlmSourceStatusResponse>>> Sources(
        CancellationToken cancellationToken)
    {
        var providerMap = _providers.ToDictionary(provider => provider.Kind);
        var installedAvailable = await _installedLocalProvider.IsAvailableAsync(cancellationToken);
        var managed = providerMap.GetValueOrDefault(LlmHarness.Core.Enums.LlmProviderKind.LocalOpenAiCompatible);
        var managedAvailable = managed is not null &&
            await managed.IsAvailableAsync(cancellationToken);

        var cloudConfigured = _openAiOptions.IsConfigured ||
            _googleGeminiOptions.IsConfigured ||
            _compatibleCloudOptions.Any(options => options.IsConfigured);
        var cloudAvailable = cloudConfigured;

        return Ok(new[]
        {
            new LlmSourceStatusResponse(
                "cloud-api",
                "Cloud API",
                "Use a hosted provider configured by the backend environment.",
                "OpenAI | GoogleGemini | Mistral | Grok",
                cloudConfigured,
                cloudAvailable,
                cloudAvailable ? null :
                    string.Join(
                        " ",
                        new[]
                        {
                            _openAiOptions.AvailabilityReason,
                            _googleGeminiOptions.AvailabilityReason
                        }
                        .Concat(_compatibleCloudOptions.Select(options => options.AvailabilityReason))
                        .Where(reason => !string.IsNullOrWhiteSpace(reason))),
                _openAiOptions.Endpoint,
                _openAiOptions.DefaultModel),
            new LlmSourceStatusResponse(
                "managed-local",
                "Download and manage a model",
                "Download the curated model, verify it, and run it locally through the managed runtime.",
                LlmHarness.Core.Enums.LlmProviderKind.LocalOpenAiCompatible.ToString(),
                managedAvailable,
                managedAvailable,
                managed is IProviderAvailabilityDetails details ? details.AvailabilityReason : null),
            new LlmSourceStatusResponse(
                "installed-local",
                "Connect to an installed local LLM",
                "Connect to an existing OpenAI-compatible local server such as Ollama or LM Studio.",
                LlmHarness.Core.Enums.LlmProviderKind.Ollama.ToString(),
                _installedLocalOptions.IsConfigured,
                installedAvailable,
                installedAvailable ? null : _installedLocalProvider.AvailabilityReason,
                _installedLocalOptions.Endpoint,
                _installedLocalOptions.Model)
        });
    }

    [HttpGet("installed-local")]
    public async Task<ActionResult<InstalledLocalSetupResponse>> InstalledLocal(
        CancellationToken cancellationToken)
    {
        var available = await _installedLocalProvider.IsAvailableAsync(cancellationToken);
        return Ok(ToInstalledLocalResponse(available));
    }

    [HttpPut("installed-local")]
    public async Task<ActionResult<InstalledLocalSetupResponse>> ConfigureInstalledLocal(
        ConfigureInstalledLocalRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint) || string.IsNullOrWhiteSpace(request.Model))
        {
            return BadRequest(new { error = "Endpoint and model are required." });
        }

        try
        {
            _installedLocalOptions.Update(request.Endpoint, request.Model);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }

        var available = await _installedLocalProvider.IsAvailableAsync(cancellationToken);
        return Ok(ToInstalledLocalResponse(available));
    }

    [HttpPost("installed-local/test")]
    public async Task<ActionResult<InstalledLocalSetupResponse>> TestInstalledLocal(
        CancellationToken cancellationToken)
    {
        var available = await _installedLocalProvider.IsAvailableAsync(cancellationToken);
        return Ok(ToInstalledLocalResponse(available));
    }

    private InstalledLocalSetupResponse ToInstalledLocalResponse(bool available) =>
        new(
            _installedLocalOptions.Endpoint,
            _installedLocalOptions.Model,
            _installedLocalOptions.IsConfigured,
            available,
            available ? null : _installedLocalProvider.AvailabilityReason);
}
