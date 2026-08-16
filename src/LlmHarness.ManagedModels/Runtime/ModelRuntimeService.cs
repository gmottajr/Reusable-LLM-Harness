using System.Diagnostics;
using LlmHarness.ManagedModels.Models;

namespace LlmHarness.ManagedModels.Runtime;

public sealed class ModelRuntimeService : IModelRuntimeService, IAsyncDisposable
{
    private readonly ManagedRuntimeOptions _options;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private string? _modelId;
    private string? _error;

    public ModelRuntimeService(ManagedRuntimeOptions options, HttpClient httpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ManagedRuntimeStatus> StartAsync(
        ManagedModelDefinition model,
        string modelPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!File.Exists(modelPath))
        {
            return Failed(model.Id, "The managed model file does not exist.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsProcessRunning() && string.Equals(_modelId, model.Id, StringComparison.OrdinalIgnoreCase))
            {
                return Running();
            }

            await StopInternalAsync();
            _error = null;
            _modelId = model.Id;

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(modelPath);
            startInfo.ArgumentList.Add("--host");
            startInfo.ArgumentList.Add(_options.BaseUri.Host);
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(_options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--alias");
            startInfo.ArgumentList.Add(model.RuntimeModelName);

            try
            {
                _process = Process.Start(startInfo);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return Failed(model.Id, $"Could not start the managed runtime: {exception.Message}");
            }

            if (_process is null)
            {
                return Failed(model.Id, "The managed runtime did not start.");
            }

            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += (_, _) => { };
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var deadline = DateTimeOffset.UtcNow + _options.StartupTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsProcessRunning())
                {
                    return Failed(model.Id, "The managed runtime exited before becoming ready.");
                }

                try
                {
                    using var response = await _httpClient.GetAsync(
                        new Uri(_options.BaseUri, "/health"),
                        cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return Running();
                    }
                }
                catch (HttpRequestException)
                {
                    // The runtime may still be loading the model.
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }

            await StopInternalAsync();
            return Failed(model.Id, "The managed runtime did not become ready before the startup timeout.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopInternalAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopInternalAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ManagedRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsProcessRunning() ? Running() :
            _error is null ? new ManagedRuntimeStatus(_modelId, ManagedRuntimeState.Stopped, _options.BaseUri) :
            Failed(_modelId, _error));
    }

    public Uri GetCompletionUri() => new(_options.BaseUri, "/v1/chat/completions");

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }

    private async Task StopInternalAsync()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited or was disposed.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private bool IsProcessRunning()
    {
        try
        {
            return _process is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private ManagedRuntimeStatus Running() =>
        new(_modelId, ManagedRuntimeState.Running, _options.BaseUri);

    private ManagedRuntimeStatus Failed(string? modelId, string error)
    {
        _error = error;
        return new(modelId, ManagedRuntimeState.Failed, _options.BaseUri, error);
    }
}
