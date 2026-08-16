# Managed Local Models (Optional Stretch)

This extension lets the API download one curated small GGUF model, verify it,
start a local OpenAI-compatible runtime, and call it through the same
`ILlmHarness` pipeline. It is optional; cloud-only use does not require a model
download or a local runtime.

## Prerequisites

Install a `llama-server` executable and make it available on `PATH`, or set:

```bash
export LLM_HARNESS_RUNTIME_EXECUTABLE=/path/to/llama-server
```

The runtime is expected to support `--model`, `--host`, `--port`, `--alias`,
`GET /health`, and `POST /v1/chat/completions`.

The catalog currently contains SmolLM2 135M Instruct in GGUF Q4_K_M form. Its
download URL and SHA-256 are pinned in code and sourced from the [curated model
file](https://huggingface.co/Mungert/SmolLM2-135M-Instruct-GGUF/blob/main/SmolLM2-135M-Instruct-q4_k_m.gguf).

## Configuration

- `LLM_HARNESS_MODEL_STORAGE` changes the managed model storage directory.
- `LLM_HARNESS_RUNTIME_EXECUTABLE` selects the runtime executable.
- `LLM_HARNESS_MANAGED_MODEL_ID` selects the catalog default.
- `LLM_HARNESS_RUNTIME_AUTO_START=true` starts the runtime on the first
  completion request when the selected model is already downloaded.

The default runtime endpoint is `http://127.0.0.1:8081`. Model files are kept
outside the source tree by default under the application data directory, and
are never committed to the repository.

## Model lifecycle API

```bash
curl http://localhost:5000/api/models
curl http://localhost:5000/api/models/smollm2-135m-instruct-q4km
curl -X POST http://localhost:5000/api/models/smollm2-135m-instruct-q4km/download
curl -X POST http://localhost:5000/api/models/smollm2-135m-instruct-q4km/start
curl -X POST http://localhost:5000/api/models/stop
```

The download endpoint reports `Downloading`, `Downloaded`, or `Failed` status
with byte counts and percentage. Poll the model status endpoint while a
download is in progress. Starting is rejected until the file exists and its
SHA-256 matches the catalog.

## Calling the managed model

Use the existing completion endpoint with the managed provider and catalog
model ID:

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

No endpoint accepts a user-supplied model URL. Adding a model requires a code
reviewed catalog entry with an allowlisted URL, filename, runtime name, and
checksum.
