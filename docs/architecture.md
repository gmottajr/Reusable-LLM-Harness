# Architecture and Design Notes

## Problem

The project provides one small application-facing interface for sending an
LLM request while keeping provider adapters, retries, timeouts, output
validation, and provider selection replaceable. Callers receive typed output or
a structured error instead of provider-specific exceptions.

## Components

```text
HTTP API
  -> API request/response mapping
  -> ILlmHarness
       -> input validation
       -> provider selection
       -> provider adapter
       -> retry policy
       -> timeout/fallback policy
       -> output deserialization
       -> optional JSON schema validation
```

`LlmHarness.Core` contains contracts and policies only. `OpenAiProvider` is an
adapter in a separate project. The API host composes services and exposes demo
endpoints; it does not contain provider completion logic.

The harness orchestration test exercises the complete path from a selected
provider response to typed output and metadata, including retry, timeout,
fallback, and schema-validation branches in focused tests.

## Strategies

- Input validation rejects empty messages, invalid roles, invalid execution
  modes, invalid timeouts, invalid temperatures, invalid token limits, and
  malformed schemas before a provider is called.
- Output validation supports a deliberately small JSON Schema subset: `type`,
  `required`, `properties`, `items`, `additionalProperties`, `enum`, and
  `const`.
- Retries cover rate limits, 408, 5xx responses, timeouts, and transient
  network failures. Validation errors and ordinary 4xx errors are not retried.
- Timeout cancellation uses a linked token. Caller cancellation is preserved,
  while timeout failures become structured results. An optional fallback is
  attempted once.
- Logs contain correlation IDs and operational metadata only. Prompts,
  responses, and credentials are excluded.

## Configuration and providers

The demo API reads `OPENAI_API_KEY` from the environment. The OpenAI adapter
also has a configurable endpoint and default model when it is registered by an
application. Provider status exposes availability and a safe reason, never the
credential value.

The managed local-model extension is optional. It owns catalog, storage,
download, and runtime lifecycle concerns outside the core. Its provider
implements `ILlmProvider` and exposes the managed runtime through the same
harness pipeline. The core does not know whether a local model was downloaded
by this app or installed by another tool.

## Adding a provider

To add a provider:

1. Create a project referencing `LlmHarness.Core`.
2. Implement `ILlmProvider` and normalize provider exceptions into
   `LlmProviderException` where appropriate.
3. Register the adapter as `ILlmProvider` in the host.
4. Add mapping, availability, error, and orchestration tests.

## Tradeoffs and limitations

The implementation favors a small dependency-free core and explicit policies
over a provider SDK abstraction with every provider feature. The schema
validator is intentionally incomplete, the API currently registers only
OpenAI, and the API returns raw text when no output schema is supplied. A
production deployment would likely add authentication, rate limiting,
request-size limits, richer provider configuration, metrics, and a complete
provider-specific schema/feature model.

## Future improvements

- Implement the local provider adapter planned for Phase 9.
- Add streaming responses and usage/cost reporting.
- Add API authentication and per-client quotas.
- Expand JSON Schema support or use a maintained schema library.
- Add integration tests against configurable local provider endpoints.
