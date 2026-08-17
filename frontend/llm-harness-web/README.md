# LLM Harness Playground

The React + Vite playground is the browser client for the LLM Harness API. It
uses a source-first workflow: select where the model lives, configure that
source, and then run requests through one shared chat surface.

## Supported sources

The page intentionally separates the three ways this application can use an
LLM:

1. **Cloud API**

   Choose OpenAI, Google Gemini, Mistral, or Grok. OpenAI uses
   `OpenAI-API-settings:ApiKey` or `OPENAI_API_KEY`; Gemini uses
   `Google-LLM:ApiKey` or
   `GOOGLE_GEMINI_API_KEY`; Mistral uses `Mistral-API-settings:ApiKey` or
   `MISTRAL_API_KEY`; and Grok uses `Grok-API-settings:XAI_API_KEY` or
   `XAI_API_KEY`. The Gemini integration calls `generateContent`; Mistral and
   Grok use their OpenAI-compatible chat-completions endpoints.

2. **Run in browser**

   Select a model from the curated catalog, download its WebLLM/MLC artifacts
   into browser storage, start or stop the WebGPU runtime, and run completions
   locally. The catalog includes SmolLM2 135M, Qwen3 0.6B, DeepSeek-R1 Distill
   Qwen 7B, Google Gemma 3 1B Instruct, Qwen Tiny 0.5B, and Qwen Small 0.6B.
   The Tiny and Small entries use q4f16 WebLLM artifacts to reduce GPU memory
   pressure. Gemma is subject to Google's Gemma terms of use.

3. **Installed local LLM**

   Connect to an existing Ollama, LM Studio, or other OpenAI-compatible local
   server. The setup panel accepts the base endpoint and model. Defaults are
   `http://127.0.0.1:11434/v1` and `llama3.2`.

## Security and credentials

The frontend does not read, display, store, or return API keys. It receives
provider status only, such as `configured` or `unavailable`. Configure cloud
credentials in the backend process using .NET User Secrets or environment
variables:

```bash
cd ../../src/LlmHarness.Api
dotnet user-secrets set 'OpenAI-API-settings:ApiKey' 'your-openai-key'
dotnet user-secrets set 'OpenAI-API-settings:Model' 'gpt-4o-mini'
dotnet user-secrets set 'Google-LLM:ApiKey' 'your-gemini-key'
dotnet user-secrets set 'Google-LLM:Model' 'gemini-flash-latest'
dotnet user-secrets set 'Mistral-API-settings:ApiKey' 'your-mistral-key'
dotnet user-secrets set 'Mistral-API-settings:Model' 'mistral-small-latest'
dotnet user-secrets set 'Grok-API-settings:XAI_API_KEY' 'your-grok-key'
dotnet user-secrets set 'Grok-API-settings:Model' 'grok-4.5'
```

The API loads these values into dependency-injected provider options. The
browser sends only the selected provider, model, prompts, and request options.
Never put credentials in this project, `appsettings.json`,
`launchSettings.json`, or frontend environment variables.

## What the page provides

- Source cards with separate readiness status for Cloud API, browser WebLLM,
  and installed local modes.
- Cloud provider selection between OpenAI, Google Gemini, Mistral, and Grok.
- Source-specific configuration panels for browser model loading, endpoint,
  start, stop, and connectivity testing.
- A curated WebLLM model selector with Qwen Tiny, Qwen Small, DeepSeek-R1
  Distill Qwen, SmolLM2, and Gemma browser-playground options. The browser loads the model through a
  dedicated Worker and never sends browser prompts to the API.
- Browser model tiers (`lightweight`, `standard`, `heavy`, and
  `experimental`), recommendation flags, estimated GPU memory, and a heavy-model
  confirmation warning.
- A styled browser-model download toast with live percentage and a progress
  bar. WebLLM reports loading progress from the Worker; model files are cached
  by the browser runtime.
- Shared system instruction, user prompt, timeout, and structured JSON schema
  controls.
- Optional advisory input-schema validation. Open **Configure** next to Input
  validation, define a flexible JSON Schema for the request envelope, and turn
  the check on or off. A mismatch is shown in the page and frontend log but
  never blocks the LLM request.
- Structured JSON defaults to a flexible `{}` schema, which accepts any valid
  JSON value. Replace it with `type`, `required`, and `properties` when strict
  response validation is needed.
- Structured result display with provider, model, duration, attempts, timeout,
  and correlation metadata.
- Structured error display for validation failures, unavailable providers,
  timeouts, and provider responses.
- Structured responses can be any JSON value, including object, array, string,
  number, boolean, and null. The backend also accepts common model formatting
  such as ```json``` fences and short explanatory text around the JSON.
- A live frontend flow log that records setup loading, source selection,
  validation, button actions, HTTP status, and completion outcomes without
  recording prompts, responses, API keys, or secrets.
- `Download .log` for saving the browser-side flow log.
- A Swagger link to the API documentation at `http://localhost:5000/`.

The matching backend flow is written to
`src/LlmHarness.Api/logs/llm-harness.log`.
Managed-model downloads log their start, throttled progress updates, checksum
verification result, completion, cancellation, or failure in that same file.
When schema validation fails, the backend log includes the raw provider
response, normalized JSON, schema, and failing JSON path for diagnosis. Treat
that file as sensitive during development because model responses may contain
private data.

The backend can also log the complete harness request and response payloads for
development diagnostics. Credential-shaped JSON fields and bearer tokens are
redacted, but prompts and completions may still contain sensitive content. Set
`LlmHarness:Logging:IncludePayloads` to `false` in User Secrets, or set
`LLM_HARNESS_LOG_PAYLOADS=false`, to return to metadata-only backend logging.

Timeout fallback is enabled by configuration rather than by the browser. Set
`LlmHarness:FallbackProvider` (for example, `GoogleGemini`) to a registered and
configured backend provider. After a timeout, the core attempts that provider
once and reports `FallbackUsed` in the result metadata. The fallback is not
silently selected when no provider is configured or available.

## Browser performance diagnostics

Browser-local completion entries in the frontend `.log` include the execution
signals needed to compare devices and models without recording prompts or
responses. A typical entry looks like:

```text
Browser completion response received model=Qwen3-0.6B-q4f16_1-MLC durationMs=21402 promptChars=74 outputChars=842 maxTokens=512 temperature=0.2 schemaEnabled=true coldStart=true tokensPerSecond=12.40 modelTier=lightweight wasCached=true timeoutMs=60000
```

The fields mean:

- `promptChars` and `outputChars`: character counts, not token counts.
- `maxTokens`, `temperature`, and `schemaEnabled`: the generation settings
  sent to WebLLM.
- `coldStart`: the completion request had to load the browser model runtime.
- `wasCached`: the model artifacts were already available in browser storage
  before that load; it is not a KV-cache hit.
- `tokensPerSecond`: WebLLM's decode rate when reported, with a measured
  fallback when token usage is available.
- `modelTier` and `timeoutMs`: the catalog classification and request limit.

This makes a slow completion diagnosable without changing the core harness
position. The backend remains the primary reusable reliability layer; WebLLM
is an optional provider adapter demonstrating that the same concept can run
entirely client-side with WebGPU.

## API calls used by the page

```text
GET  /api/setup/sources
GET  /api/models                 (catalog metadata and browserModelId)
GET  /api/setup/installed-local
PUT  /api/setup/installed-local
POST /api/setup/installed-local/test
POST /api/llm/complete
```

The Vite development server proxies `/api` and `/health` to
`http://localhost:5000`.

## Screen-by-screen workflow

### 1. Choose an LLM source

The source cards select exactly one execution path:

- **Cloud API**: choose OpenAI, Google Gemini, Mistral, or Grok. The API reads
  the secret and calls the provider from the backend.
- **Run in browser**: select a curated WebLLM model, load it into browser
  storage, start or stop the WebGPU runtime, and run the completion locally.
- **Installed local LLM**: enter an existing OpenAI-compatible server URL and
  model, then save and test the connection.

### 2. Configure the active source

Only the setup panel for the selected source is shown. Status and readiness are
reported by backend endpoints. Provider endpoints shown in the Cloud API panel
are informational; the browser does not call them directly.

### 3. Test the active source

The shared request panel is used for all three paths. Set the model, timeout,
system instruction, user prompt, and output schema, then select **Run
completion**. The default `{}` schema accepts any valid JSON. Enter a stricter
schema only when the response must contain specific fields.

Input validation is separate and advisory. Use **Configure** to open the input
schema dialog, enable the check, and define a schema for the outgoing request
envelope (`provider`, `model`, `timeoutMs`, and `messages`). If the schema is
invalid or the request does not match it, the page reports the issue and still
continues with the LLM call. This keeps exploratory provider testing
non-blocking while making input-shape problems visible.

### 4. Inspect the result and logs

The result panel displays the response and execution metadata. The frontend
flow log records setup loading, source selection, button actions, HTTP status,
and completion outcomes. Use **Download .log** to save it. The backend log at
`src/LlmHarness.Api/logs/llm-harness.log` includes correlation IDs, provider
errors, raw provider responses, normalized JSON, schemas, and validation paths
when diagnosing a failure.

## Run locally

Start the API in one terminal:

```bash
dotnet run --project ../../src/LlmHarness.Api --urls http://localhost:5000
```

Start the frontend in this directory from a second terminal:

```bash
npm install
npm run dev
```

Open [http://localhost:5173](http://localhost:5173).

If you use `--no-launch-profile` for the API, enable User Secrets explicitly:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project ../../src/LlmHarness.Api \
  --no-launch-profile --urls http://localhost:5000
```

For a separately hosted API, set `VITE_API_BASE_URL` before building:

```bash
VITE_API_BASE_URL=https://api.example.test npm run build
```

Build the frontend locally with:

```bash
npm run build
```
