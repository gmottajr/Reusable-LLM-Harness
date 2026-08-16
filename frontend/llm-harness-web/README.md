# LLM Harness Playground

This optional React + Vite playground demonstrates the existing API. It keeps
provider selection, validation, retries, timeouts, and secrets in the backend.

## Run locally

In one terminal, start the API on the Vite proxy target:

```bash
dotnet run --project ../../src/LlmHarness.Api --urls http://localhost:5000
```

In another terminal:

```bash
npm install
npm run dev
```

Open `http://localhost:5173`. The Vite dev server proxies `/api` and `/health`
to `http://localhost:5000`.

For a separately hosted API, set `VITE_API_BASE_URL` before building:

```bash
VITE_API_BASE_URL=https://api.example.test npm run build
```

The frontend never accepts or stores `OPENAI_API_KEY`; configure that secret in
the API process instead.
