# Managed Local Models (Backend Compatibility)

The playground's **Run in browser** path does not use this backend runtime. It
loads WebLLM/MLC artifacts in a browser Worker and executes them with WebGPU.
This document describes the optional backend compatibility API for deployments
that intentionally want to run a GGUF model through an installed
`llama-server` process instead.

## Prerequisites

Install a `llama-server` executable and make it available on `PATH`, or set:

```bash
export LLM_HARNESS_RUNTIME_EXECUTABLE=/path/to/llama-server
```

The runtime is expected to support `--model`, `--host`, `--port`, `--alias`,
`GET /health`, and `POST /v1/chat/completions`.

The catalog contains pinned GGUF compatibility entries. The browser mappings
are exposed separately as `browserModelId` values in `GET /api/models`; those
IDs are consumed by the WebLLM frontend and are not GGUF files.

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

The compatibility download endpoint reports `Downloading`, `Downloaded`, or `Failed` status
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

## Browser path

For the frontend, choose **Run in browser**. The browser model identifiers
currently map to WebLLM artifacts such as `SmolLM2-135M-Instruct-q0f32-MLC`,
`Qwen2.5-0.5B-Instruct-q4f16_1-MLC` (Qwen Tiny),
`Qwen3-0.6B-q4f16_1-MLC` (Qwen Small),
`DeepSeek-R1-Distill-Qwen-7B-q4f16_1-MLC`, and
`gemma3-1b-it-q4f16_1-MLC`. No `llama-server` executable is required for that
path, and the browser sends neither prompts nor completions to the API.

Browser catalog entries also expose `browserTier`, `browserRecommended`,
`browserVramRequiredMb`, and an optional `browserWarning`. The frontend uses
these fields to guide model selection and asks for confirmation before loading
heavy models such as DeepSeek-R1 Distill Qwen 7B.
