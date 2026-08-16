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

The shortest path to experiencing the project is to run the API and call it
with `curl`. You need the .NET 8 SDK, `curl`, and optionally `jq` for readable
JSON extraction.

### 1. Clone and enter the repository

```bash
git clone https://github.com/gmottajr/Reusable-LLM-Harness.git
cd Reusable-LLM-Harness
```

If you are working from an existing checkout, start from its root instead.

### 2. Restore, build, and test

```bash
dotnet restore
dotnet build
dotnet test
```

The tests are focused on the reliability behavior that matters most: input and
output validation, retries, non-retryable errors, timeouts, fallback,
provider selection, provider mapping, managed-model safety, orchestration, and
API behavior.

### 3. Configure OpenAI

The demo API reads the key from the process environment. Set it only in your
local shell or secret manager; do not put it in a file committed to Git.

```bash
export OPENAI_API_KEY='your-api-key'
```

The API can still start without a key. In that case OpenAI appears as
unavailable and requests that select it return a structured provider error.

### 4. Start the API

Use a fixed local URL so the `curl` examples and React playground use the same
address:

```bash
dotnet run --project src/LlmHarness.Api --urls http://localhost:5000
```

Keep this terminal running. Open a second terminal in the repository root for
the remaining steps.

### 5. Check that the API is alive

```bash
curl -sS http://localhost:5000/health
```

Expected result:

```json
{"status":"healthy"}
```

Check provider availability without exposing credentials:

```bash
curl -sS http://localhost:5000/api/providers/status | jq .
```

With `OPENAI_API_KEY` set, OpenAI should report `available: true`. Without it,
the response explains that the provider is unavailable but never returns the
key itself. The managed local provider is unavailable until its model has been
downloaded and its runtime started, unless managed runtime auto-start is
enabled.

### 6. Submit a structured completion

Create a request file:

```bash
cat > request.json <<'JSON'
{
  "provider": "OpenAI",
  "model": "gpt-4.1-mini",
  "timeoutMs": 10000,
  "messages": [
    {"role": "system", "content": "Return JSON only."},
    {"role": "user", "content": "Give me a fictional engineer name and role."}
  ],
  "outputSchema": {
    "type": "object",
    "required": ["name", "role"],
    "properties": {
      "name": {"type": "string"},
      "role": {"type": "string"}
    }
  }
}
JSON
```

Send it to the API:

```bash
curl -sS -X POST http://localhost:5000/api/llm/complete \
  -H 'Content-Type: application/json' \
  --data @request.json | jq .
```

The API maps this HTTP payload into the provider-independent `LlmRequest` and
passes it to `ILlmHarness`. The API does not call OpenAI directly.

### 7. Extract and understand the result

A successful response has this shape:

```json
{
  "success": true,
  "data": {"name": "Ada", "role": "Engineer"},
  "error": null,
  "metadata": {
    "provider": "OpenAI",
    "model": "gpt-4.1-mini",
    "attempts": 1,
    "retryCount": 0,
    "durationMs": 742.31,
    "timeoutMs": 10000,
    "fallbackUsed": false,
    "correlationId": "..."
  }
}
```

Save one response and inspect the pieces independently. Saving the response
also avoids sending the same paid request three times:

```bash
response=$(curl -sS -X POST http://localhost:5000/api/llm/complete \
  -H 'Content-Type: application/json' --data @request.json)

# The typed/structured model output
echo "$response" | jq '.data'

# Provider, timing, retry, and fallback behavior
echo "$response" | jq '.metadata'

# Failure details, when success is false
echo "$response" | jq '.error'
```

Important metadata fields:

- `provider` and `model` show what actually handled the request.
- `attempts` and `retryCount` show transient-failure recovery.
- `durationMs` and `timeoutMs` show execution timing and the applied limit.
- `fallbackUsed` shows whether the optional fallback path ran.
- `correlationId` identifies the request in safe operational logs.

The HTTP status also communicates the broad failure class: malformed input is
`400`, an unavailable provider is `503`, a timeout is `504`, and downstream
provider/output failures use a structured error envelope.

### 8. See validation fail before a provider call

Change the request to use an invalid role:

```bash
curl -sS -X POST http://localhost:5000/api/llm/complete \
  -H 'Content-Type: application/json' \
  -d '{"messages":[{"role":"not-a-role","content":"hello"}]}' | jq .
```

The response is HTTP `400` with `error.type` equal to
`InputValidationError`. This demonstrates the main safety boundary: invalid
requests are rejected before a provider is called.

## What this project is demonstrating

The main purpose is not to hide an SDK call behind another method. It is to
provide a small reliability and safety layer around LLM calls:

```text
application request
  -> input validation
  -> provider selection
  -> retry transient failures
  -> enforce timeout
  -> use optional fallback
  -> deserialize typed output
  -> validate output schema
  -> return structured result and safe metadata
```

To experience that flow, compare the same completion request across a
configured OpenAI provider and the optional managed local provider. The
application-facing contract stays the same while the provider, timing,
attempts, validation, and failure metadata remain visible in the response.

## Optional: use the React playground

With the API still running on port `5000`, open a third terminal:

```bash
cd frontend/llm-harness-web
npm install
npm run dev
```

Open `http://localhost:5173`. The Vite server proxies `/api` to the .NET API.
The page lets you choose a provider, enter a model and prompt, edit the output
schema, submit a request, inspect provider availability, and view result
metadata. It contains no API-key input; credentials remain in the backend
process.

## Optional: use a managed local model

The managed local model extension is not required for the OpenAI walkthrough.
To try it, install a compatible `llama-server` executable, then set its path:

```bash
export LLM_HARNESS_RUNTIME_EXECUTABLE=/path/to/llama-server
```

Start the API, list the curated catalog, download the supported model, and
start the runtime:

```bash
curl -sS http://localhost:5000/api/models | jq .
curl -sS -X POST \
  http://localhost:5000/api/models/smollm2-135m-instruct-q4km/download | jq .
curl -sS -X POST \
  http://localhost:5000/api/models/smollm2-135m-instruct-q4km/start | jq .
```

Then change `request.json` to:

```json
{
  "provider": "LocalOpenAiCompatible",
  "model": "smollm2-135m-instruct-q4km",
  "timeoutMs": 30000,
  "messages": [
    {"role": "user", "content": "Return a short JSON greeting."}
  ],
  "outputSchema": {
    "type": "object",
    "required": ["message"],
    "properties": {"message": {"type": "string"}}
  }
}
```

Submit it with the same completion command. The model must come from the
curated catalog and pass checksum verification; arbitrary download URLs are
not accepted. See [Managed local models](docs/managed-models.md) for lifecycle
configuration and runtime details.

## Documentation

- [Architecture and design notes](docs/architecture.md)
- [API examples and configuration](docs/api-examples.md)
- [Managed local models](docs/managed-models.md)
- [React playground](frontend/llm-harness-web/README.md)
