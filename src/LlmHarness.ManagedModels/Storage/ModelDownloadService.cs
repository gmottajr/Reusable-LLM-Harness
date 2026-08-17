using System.Collections.Concurrent;
using LlmHarness.ManagedModels.Catalog;
using LlmHarness.ManagedModels.Models;

namespace LlmHarness.ManagedModels.Storage;

public sealed class ModelDownloadService : IModelDownloadService
{
    private readonly IModelCatalogService _catalog;
    private readonly IModelStorageService _storage;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, ManagedModelStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public ModelDownloadService(
        IModelCatalogService catalog,
        IModelStorageService storage,
        HttpClient httpClient)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ManagedModelStatus> GetStatusAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        var model = GetModel(modelId);
        if (_statuses.TryGetValue(model.Id, out var status) &&
            status.State is ManagedModelState.Downloading or ManagedModelState.Failed)
        {
            return status;
        }

        var stored = await _storage.InspectAsync(model, cancellationToken);
        return stored.IsValid
            ? Status(model, ManagedModelState.Downloaded, stored.BytesDownloaded, stored.BytesDownloaded, 100)
            : Status(model, ManagedModelState.NotDownloaded, stored.BytesDownloaded, model.SizeBytes, 0, stored.Error);
    }

    public async Task<ManagedModelStatus> DownloadAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        var model = GetModel(modelId);
        var gate = _locks.GetOrAdd(model.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _storage.InspectAsync(model, cancellationToken);
            if (existing.IsValid)
            {
                return SetStatus(Status(model, ManagedModelState.Downloaded, existing.BytesDownloaded, model.SizeBytes, 100));
            }

            SetStatus(Status(model, ManagedModelState.Downloading, 0, model.SizeBytes, 0));
            using var response = await _httpClient.GetAsync(
                model.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var totalBytes = response.Content.Headers.ContentLength ?? model.SizeBytes;

            await _storage.SaveAsync(
                model,
                content,
                totalBytes,
                new Progress<ModelDownloadProgress>(progress =>
                    SetStatus(Status(
                        model,
                        ManagedModelState.Downloading,
                        progress.BytesDownloaded,
                        progress.TotalBytes,
                        progress.Percentage))),
                cancellationToken);

            var storedAfterDownload = await _storage.InspectAsync(model, cancellationToken);
            return SetStatus(Status(
                model,
                ManagedModelState.Downloaded,
                storedAfterDownload.BytesDownloaded,
                model.SizeBytes,
                100));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(Status(model, ManagedModelState.Failed, error: "Model download was canceled."));
            throw;
        }
        catch (Exception exception)
        {
            return SetStatus(Status(model, ManagedModelState.Failed, error: exception.Message));
        }
        finally
        {
            gate.Release();
        }
    }

    private ManagedModelDefinition GetModel(string modelId) =>
        _catalog.Find(modelId) ?? throw new KeyNotFoundException($"Managed model '{modelId}' is not in the curated catalog.");

    private ManagedModelStatus SetStatus(ManagedModelStatus status)
    {
        _statuses[status.ModelId] = status;
        return status;
    }

    private static ManagedModelStatus Status(
        ManagedModelDefinition model,
        ManagedModelState state,
        long bytesDownloaded = 0,
        long? totalBytes = null,
        double percentage = 0,
        string? error = null) =>
        new(model.Id, state, bytesDownloaded, totalBytes, percentage, error, DateTimeOffset.UtcNow);
}
