# Small Reusable LLM Harness

This repository contains a backend-first .NET 8 implementation of a small,
provider-agnostic LLM harness. It solves the repeated application problem of
calling different LLM providers with consistent validation, retries, timeouts,
fallbacks, typed output, and structured errors. Application code depends on
contracts in `LlmHarness.Core`; provider adapters and the demo API remain
separate projects.

## Current scope

The project includes the core MVP, a minimal API demo, and an optional managed
local-model extension. Phase 10 adds centralized provider selection:

- `ILlmHarness` for application-facing execution.
- `ILlmProvider` for provider adapters.
- Provider-independent request, response, result, error, and metadata models.
- Provider, execution-mode, message-role, and error-type enums.
- `ILlmRequestValidator` and `LlmRequestValidator` for fail-fast validation.
- `ValidatingLlmHarness`, which rejects invalid requests before delegating to
  the provider-facing harness.
- Structured, non-retryable `InputValidationError` results.
- `ISchemaValidator` and `JsonSchemaValidator` for common JSON Schema rules.
- `SchemaValidatingLlmHarness`, which validates successful JSON output before
  returning it to callers.
- `LlmRetryPolicy` and `RetryingLlmHarness` for transient provider failures.
- Retry configuration with 3 retries, 500 ms initial delay, 5 s maximum delay,
  and optional jitter by default.
- Attempt and retry counts in `LlmMetadata`.
- `LlmTimeoutOptions` for configurable default timeouts.
- `TimeoutLlmHarness` for linked-token cancellation and structured timeout
  failures.
- Optional one-attempt fallback harness support.
- Duration, timeout, fallback, and selected-provider metadata.
- `LlmHarness` for provider selection, validation, retries, timeout/fallback,
  output validation, and safe metadata-only logging.
- `OpenAiProvider` using `IHttpClientFactory` or an injected `HttpClient`.
- `OpenAiOptions` with `OPENAI_API_KEY` environment-variable support.
- OpenAI request/response mapping and normalized provider exceptions.
- `IProviderSelector` and `ProviderSelector` for manual, cloud-preferred, and
  local-preferred selection.
- `ProviderSelectionOptions` with `AutoPreferCloud` as the default mode.
- A minimal API host with `GET /health`, `GET /api/providers/status`, and
  `POST /api/llm/complete`.
- API request mapping, malformed-request validation, structured result mapping,
  and secret-safe provider status reporting.
- Optional managed-model catalog, checksum-verified downloads, runtime startup,
  local OpenAI-compatible provider, and model-management endpoints.
- Optional React + Vite playground for composing requests and inspecting
  results without exposing provider credentials.
- Contract, input validation, schema validation, retry, timeout, fallback,
  provider-selection, orchestration, and API smoke tests.

The managed local-model extension is explicitly optional and does not change the
core harness contracts. A standard externally managed local provider remains a
separate integration choice.
The built-in schema validator supports `type`, `required`,
`properties`, `items`, `additionalProperties`, `enum`, and `const`.

Retrying is limited to rate limits, 5xx responses, request timeouts, and
transient network failures. Client errors such as 400 and 401, validation
errors, and output schema errors are not retried.

When a request times out, the harness returns a structured `TimeoutError`. If a
fallback harness is configured, it is attempted once and `FallbackUsed` is set
in the result metadata.

The orchestration logger receives correlation IDs and provider/model/result
metadata only; prompts, responses, and credentials are not included.

The OpenAI provider reads `OPENAI_API_KEY` from the environment. Missing keys
make the provider unavailable, and provider error messages redact the configured
key before they become exceptions or harness results.

## Project layout

```text
src/
  LlmHarness.Api/
  LlmHarness.Core/
  LlmHarness.ManagedModels/
  LlmHarness.Providers.OpenAI/
  LlmHarness.Providers.Local/
frontend/
  llm-harness-web/
tests/
  LlmHarness.Tests/
```

The core project has no reference to provider-specific projects or SDKs.

## Run locally

Requires the .NET 8 SDK.

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/LlmHarness.Api
```

The health endpoint is available at `http://localhost:5000/health` (or the
URL printed by ASP.NET Core) and returns:

```json
{"status":"healthy"}
```

The completion demo accepts provider, model, timeout, messages, and an optional
JSON output schema. Provider selection and execution remain inside
`ILlmHarness`; the API host only maps HTTP requests and responses.

## Documentation

- [Architecture and design notes](docs/architecture.md)
- [API examples and configuration](docs/api-examples.md)
- [Managed local models](docs/managed-models.md)
- [React playground](frontend/llm-harness-web/README.md)
