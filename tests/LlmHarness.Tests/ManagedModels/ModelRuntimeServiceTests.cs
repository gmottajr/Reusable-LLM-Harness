using LlmHarness.ManagedModels.Models;
using LlmHarness.ManagedModels.Runtime;

namespace LlmHarness.Tests.ManagedModels;

public sealed class ModelRuntimeServiceTests
{
    [Fact]
    public async Task Missing_runtime_executable_returns_failed_status()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), $"llm-harness-runtime-test-{Guid.NewGuid():N}.gguf");
        await File.WriteAllTextAsync(modelPath, "fixture");
        var model = new ManagedModelDefinition(
            "fixture-model",
            "Fixture",
            "Tests",
            "Test model",
            new Uri("https://example.test/fixture.gguf"),
            "fixture.gguf",
            7,
            new string('0', 64),
            "fixture",
            "Test");

        try
        {
            var service = new ModelRuntimeService(
                new ManagedRuntimeOptions
                {
                    ExecutablePath = Path.Combine(Path.GetTempPath(), "missing-llama-server"),
                    StartupTimeout = TimeSpan.FromMilliseconds(50)
                },
                new HttpClient());

            var status = await service.StartAsync(model, modelPath);

            Assert.Equal(ManagedRuntimeState.Failed, status.State);
            Assert.Contains("Could not start", status.Error!);
            await service.DisposeAsync();
        }
        finally
        {
            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }
        }
    }
}
