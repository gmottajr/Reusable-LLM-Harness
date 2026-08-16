using System.Security.Cryptography;
using System.Text;
using LlmHarness.ManagedModels.Models;
using LlmHarness.ManagedModels.Storage;

namespace LlmHarness.Tests.ManagedModels;

public sealed class ModelStorageServiceTests
{
    [Fact]
    public async Task Save_verifies_checksum_before_committing_model()
    {
        var content = Encoding.UTF8.GetBytes("curated model fixture");
        var root = Path.Combine(Path.GetTempPath(), $"llm-harness-model-test-{Guid.NewGuid():N}");
        var model = CreateModel(content);

        try
        {
            var storage = new ModelStorageService(root);
            await storage.SaveAsync(model, new MemoryStream(content), content.Length);

            var status = await storage.InspectAsync(model);

            Assert.True(status.IsPresent);
            Assert.True(status.IsValid);
            Assert.Equal((long)content.Length, status.BytesDownloaded);
            Assert.True(File.Exists(storage.GetModelPath(model)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_rejects_checksum_mismatch_without_leaving_a_model_file()
    {
        var content = Encoding.UTF8.GetBytes("untrusted fixture");
        var root = Path.Combine(Path.GetTempPath(), $"llm-harness-model-test-{Guid.NewGuid():N}");
        var model = CreateModel(Encoding.UTF8.GetBytes("different content"));

        try
        {
            var storage = new ModelStorageService(root);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                storage.SaveAsync(model, new MemoryStream(content), content.Length));

            Assert.False(File.Exists(storage.GetModelPath(model)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ManagedModelDefinition CreateModel(byte[] content) =>
        new(
            "fixture-model",
            "Fixture",
            "Tests",
            "Test model",
            new Uri("https://example.test/fixture.gguf"),
            "fixture.gguf",
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            "fixture",
            "Test");
}
