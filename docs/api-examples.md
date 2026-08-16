# API Examples

## Run the project

From the repository root:

```bash
dotnet restore
dotnet test
OPENAI_API_KEY=your-key dotnet run --project src/LlmHarness.Api --urls http://localhost:5000
```

Do not commit the key. Set it in the shell, a local secret manager, or the
process environment used to launch the API.

## Health

```bash
curl http://localhost:5000/health
```

```json
{"status":"healthy"}
```

## Provider status

```bash
curl http://localhost:5000/api/providers/status
```

Example response when OpenAI is not configured:

```json
[
  {
    "provider": "OpenAI",
    "available": false,
    "reason": "Missing OPENAI_API_KEY environment variable."
  }
]
```

The status response contains availability metadata only; it never returns the
API key.

## Completion

```bash
curl -X POST http://localhost:5000/api/llm/complete \
  -H 'Content-Type: application/json' \
  -d @request.json
```

`request.json`:

```json
{
  "provider": "OpenAI",
  "model": "gpt-4.1-mini",
  "timeoutMs": 10000,
  "messages": [
    {"role": "system", "content": "Return JSON only."},
    {"role": "user", "content": "Give me a name and role."}
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
```

Successful responses have this shape:

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

Malformed requests return HTTP 400 with the same envelope and an
`InputValidationError`. Provider unavailability returns HTTP 503; timeouts
return HTTP 504.

## Managed local models

The optional managed-model extension is documented in
[Managed local models](managed-models.md). It uses catalog IDs rather than
accepting arbitrary download URLs.
