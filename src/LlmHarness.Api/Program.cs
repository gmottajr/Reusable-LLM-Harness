using LlmHarness.Api.Configuration;
using LlmHarness.Api.Models;
using LlmHarness.Core.Interfaces;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddLlmHarnessApi();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/providers/status", async (
    IEnumerable<ILlmProvider> providers,
    CancellationToken cancellationToken) =>
{
    var statuses = new List<ApiProviderStatusResponse>();
    foreach (var provider in providers)
    {
        var details = provider as IProviderAvailabilityDetails;
        bool available;
        string? reason = null;

        try
        {
            available = await provider.IsAvailableAsync(cancellationToken);
            reason = available ? null : details?.AvailabilityReason ?? "Provider unavailable.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            available = false;
            reason = details?.AvailabilityReason ?? "Provider unavailable.";
        }

        statuses.Add(new ApiProviderStatusResponse(
            provider.Kind.ToString(),
            available,
            reason));
    }

    return Results.Ok(statuses);
});

app.MapPost("/api/llm/complete", ApiEndpointHandlers.CompleteAsync);
app.MapControllers();

app.Run();

public partial class Program
{
}
