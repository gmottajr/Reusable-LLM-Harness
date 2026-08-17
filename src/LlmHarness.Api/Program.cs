using LlmHarness.Api.Configuration;
using LlmHarness.Api.Logging;
using LlmHarness.Api.Models;
using LlmHarness.Core.Interfaces;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

var logFilePath = Environment.GetEnvironmentVariable("LLM_HARNESS_LOG_FILE") ??
    Path.Combine(builder.Environment.ContentRootPath, "logs", "llm-harness.log");
builder.Logging.AddProvider(new FileLoggerProvider(logFilePath));
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LLM Harness API",
        Version = "v1",
        Description = "Provider-agnostic LLM execution with validation, retries, timeouts, fallback, and managed local models."
    });
});
builder.Services.AddLlmHarnessApi(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("LlmHarness.Api.Exceptions");
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        logger.LogError(
            exception,
            "UnhandledException method={Method} path={Path} traceId={TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "InternalServerError",
                message = "The API encountered an unexpected error.",
                traceId = context.TraceIdentifier
            });
        }
    });
});

app.Use(async (context, next) =>
{
    var logger = context.RequestServices
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("LlmHarness.Api.Requests");
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    logger.LogInformation(
        "RequestStarted method={Method} path={Path} traceId={TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.TraceIdentifier);

    await next();

    logger.LogInformation(
        "RequestCompleted method={Method} path={Path} statusCode={StatusCode} durationMs={DurationMs} traceId={TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
        context.TraceIdentifier);
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LLM Harness API v1");
    options.RoutePrefix = string.Empty;
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithTags("System")
    .WithSummary("Check API health");

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
})
    .WithTags("Providers")
    .WithSummary("Get provider availability without exposing credentials");

app.MapPost("/api/llm/complete", ApiEndpointHandlers.CompleteAsync)
    .WithTags("LLM")
    .WithSummary("Execute a typed LLM completion");
app.MapControllers();

app.Run();

public partial class Program
{
}
