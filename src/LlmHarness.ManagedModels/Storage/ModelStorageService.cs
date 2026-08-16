using System.Security.Cryptography;
using LlmHarness.ManagedModels.Models;

namespace LlmHarness.ManagedModels.Storage;

public sealed class ModelStorageService : IModelStorageService
{
    private readonly string _rootPath;

    public ModelStorageService(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Model storage path is required.", nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public string GetModelPath(ManagedModelDefinition model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var modelDirectory = SafePath(model.Id);
        Directory.CreateDirectory(modelDirectory);
        return SafePath(model.Id, model.FileName);
    }

    public async Task<ModelStorageStatus> InspectAsync(
        ManagedModelDefinition model,
        CancellationToken cancellationToken = default)
    {
        var path = GetModelPath(model);
        if (!File.Exists(path))
        {
            return new(false, false, 0);
        }

        var fileInfo = new FileInfo(path);
        if (model.SizeBytes is { } expectedSize && fileInfo.Length != expectedSize)
        {
            return new(true, false, fileInfo.Length, "Stored model size does not match the catalog.");
        }

        var hash = await ComputeHashAsync(path, cancellationToken);
        return string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase)
            ? new(true, true, fileInfo.Length)
            : new(true, false, fileInfo.Length, "Stored model checksum does not match the catalog.");
    }

    public async Task SaveAsync(
        ManagedModelDefinition model,
        Stream content,
        long? totalBytes,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(content);

        var finalPath = GetModelPath(model);
        var temporaryPath = $"{finalPath}.{Guid.NewGuid():N}.partial";
        var bytesDownloaded = 0L;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    bytesDownloaded += read;
                    progress?.Report(new(
                        bytesDownloaded,
                        totalBytes,
                        totalBytes is > 0
                            ? Math.Min(100, bytesDownloaded * 100d / totalBytes.Value)
                            : 0));
                }

                await output.FlushAsync(cancellationToken);
            }

            if (model.SizeBytes is { } expectedSize && bytesDownloaded != expectedSize)
            {
                throw new InvalidDataException("Downloaded model size does not match the catalog.");
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actualHash, model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Downloaded model checksum does not match the catalog.");
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string SafePath(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(new[] { _rootPath }.Concat(parts).ToArray()));
        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.Ordinal) &&
            !string.Equals(path, _rootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Model path escapes the configured storage directory.");
        }

        return path;
    }

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
