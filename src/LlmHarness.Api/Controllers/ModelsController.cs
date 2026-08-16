using LlmHarness.ManagedModels.Catalog;
using LlmHarness.ManagedModels.Models;
using LlmHarness.ManagedModels.Runtime;
using LlmHarness.ManagedModels.Storage;
using Microsoft.AspNetCore.Mvc;

namespace LlmHarness.Api.Controllers;

[ApiController]
[Route("api/models")]
public sealed class ModelsController : ControllerBase
{
    private readonly IModelCatalogService _catalog;
    private readonly IModelDownloadService _downloads;
    private readonly IModelStorageService _storage;
    private readonly IModelRuntimeService _runtime;

    public ModelsController(
        IModelCatalogService catalog,
        IModelDownloadService downloads,
        IModelStorageService storage,
        IModelRuntimeService runtime)
    {
        _catalog = catalog;
        _downloads = downloads;
        _storage = storage;
        _runtime = runtime;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ManagedModelApiResponse>>> List(
        CancellationToken cancellationToken)
    {
        var runtime = await _runtime.GetStatusAsync(cancellationToken);
        var response = new List<ManagedModelApiResponse>();
        foreach (var model in _catalog.GetAll())
        {
            var download = await _downloads.GetStatusAsync(model.Id, cancellationToken);
            response.Add(ToResponse(model, download, runtime));
        }

        return Ok(response);
    }

    [HttpGet("{modelId}")]
    public async Task<ActionResult<ManagedModelApiResponse>> Get(
        string modelId,
        CancellationToken cancellationToken)
    {
        var model = _catalog.Find(modelId);
        if (model is null)
        {
            return NotFound(new { error = "The requested model is not in the curated catalog." });
        }

        return Ok(ToResponse(
            model,
            await _downloads.GetStatusAsync(model.Id, cancellationToken),
            await _runtime.GetStatusAsync(cancellationToken)));
    }

    [HttpPost("{modelId}/download")]
    public async Task<ActionResult<ManagedModelStatus>> Download(
        string modelId,
        CancellationToken cancellationToken)
    {
        if (_catalog.Find(modelId) is null)
        {
            return NotFound(new { error = "The requested model is not in the curated catalog." });
        }

        return Ok(await _downloads.DownloadAsync(modelId, cancellationToken));
    }

    [HttpPost("{modelId}/start")]
    public async Task<ActionResult<ManagedRuntimeStatus>> Start(
        string modelId,
        CancellationToken cancellationToken)
    {
        var model = _catalog.Find(modelId);
        if (model is null)
        {
            return NotFound(new { error = "The requested model is not in the curated catalog." });
        }

        var stored = await _storage.InspectAsync(model, cancellationToken);
        if (!stored.IsValid)
        {
            return Conflict(new
            {
                error = "Download and verify the model before starting the managed runtime."
            });
        }

        var status = await _runtime.StartAsync(
            model,
            _storage.GetModelPath(model),
            cancellationToken);
        if (status.State != ManagedRuntimeState.Running)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, status);
        }

        return Ok(status);
    }

    [HttpPost("stop")]
    public async Task<ActionResult<ManagedRuntimeStatus>> Stop(
        CancellationToken cancellationToken)
    {
        await _runtime.StopAsync(cancellationToken);
        return Ok(await _runtime.GetStatusAsync(cancellationToken));
    }

    private static ManagedModelApiResponse ToResponse(
        ManagedModelDefinition model,
        ManagedModelStatus download,
        ManagedRuntimeStatus runtime)
    {
        var state = download.State;
        if (runtime.State == ManagedRuntimeState.Running &&
            string.Equals(runtime.ModelId, model.Id, StringComparison.OrdinalIgnoreCase))
        {
            state = ManagedModelState.Running;
        }

        return new ManagedModelApiResponse(
            model.Id,
            model.Name,
            model.Creator,
            model.Description,
            model.SizeBytes,
            model.License,
            state.ToString(),
            download.BytesDownloaded,
            download.TotalBytes,
            download.Percentage,
            download.Error,
            runtime.State == ManagedRuntimeState.Running &&
                string.Equals(runtime.ModelId, model.Id, StringComparison.OrdinalIgnoreCase));
    }

    public sealed record ManagedModelApiResponse(
        string Id,
        string Name,
        string Creator,
        string Description,
        long? SizeBytes,
        string License,
        string State,
        long BytesDownloaded,
        long? TotalBytes,
        double Percentage,
        string? Error,
        bool RuntimeRunning);
}
