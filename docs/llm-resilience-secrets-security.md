# LLM Resilience, Secrets, and Security — Conceptual Design

## Question

Answer in writing. Be specific: cite tools, patterns, and real tradeoffs.

1. How do you design fallback and retry logic when an LLM provider goes down
   or hits severe rate limits in production? Walk through your
   circuit-breaker strategy.
2. How are API keys and secrets stored, accessed, and rotated in your builds?
   How do you scope permissions to the minimum required for each service?
3. What attack surfaces would you expect in a system like the one described
   above, and what would you prioritize mitigating first?

## 1. Fallback, retry logic, and circuit breaking

I would isolate all LLM access behind a provider-agnostic orchestration layer.
Application services would depend on an `ILlmProvider` or equivalent contract,
not on a specific provider SDK. The orchestration layer owns provider routing,
timeouts, retry budgets, circuit state, fallback compatibility, admission
control, idempotency, and operational telemetry.

In a .NET implementation I would use
[`Microsoft.Extensions.Http.Resilience`](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience),
which uses the Polly resilience ecosystem. Its HTTP resilience strategies
include rate limiting, total and per-attempt timeouts, retry, and circuit
breaking. I would configure one coherent resilience handler per HTTP client
rather than stacking independent handlers. Fallback to another provider is an
orchestrator/routing decision above the provider HTTP pipeline, not something
that should blindly repeat every request.

The conceptual pipeline is:

```text
Tenant admission and outbound quota check
  -> total request deadline
  -> bounded concurrency / bulkhead
  -> one provider attempt
  -> bounded retry for eligible transient failures
  -> provider/deployment circuit breaker
  -> one compatible fallback decision
  -> durable queue or controlled degraded response
```

The effective ordering and timeout values should be verified against the
library version in use. Retry and timeout policies must share one total
deadline; otherwise three retries can turn a nominal five-second request into
a minute-long request.

### Failure classification

I would classify outcomes before retrying:

| Outcome | Action |
|---|---|
| Network failure, connection reset, timeout, 408, 502, 503, 504 | Retry within the request deadline using exponential backoff and jitter |
| 429 caused by a temporary provider quota or overload | Honor `Retry-After`, apply a bounded delay, and possibly queue or fail over |
| Invalid request, unsupported model, malformed schema, policy rejection | Do not retry; return a deterministic error |
| Invalid credentials or authorization failure | Do not retry repeatedly; alert and route to credential/configuration remediation |
| Caller cancellation or expired deadline | Stop immediately; do not retry |
| Provider monthly quota exhausted | Do not keep retrying; disable that route until quota recovery or operator action |

`Retry-After` is a server hint, not permission to wait forever. I would cap
the delay, enforce a total request deadline, and use a durable queue for work
that can tolerate delay.

I would also distinguish a tenant-specific `429` from a systemic provider
failure. A single tenant exceeding its allocation should be handled by that
tenant’s limiter and budget, not by opening a circuit for every tenant. A
systemic pattern of 429s across tenants and deployments may contribute to a
provider circuit opening.

### Circuit-breaker strategy

The breaker is maintained per meaningful dependency route, such as:

```text
provider + region/deployment + model family + operation class
```

I would not use one global breaker for every model and operation. A failure in
a large summarization model should not necessarily block a small interactive
classification model.

The states are:

```text
Closed:
  Send traffic normally and record eligible outcomes.

Open:
  Fail fast or route away from the dependency. Do not send normal traffic to
  the provider during the cool-down period.

Half-open:
  Permit a small, single-flight number of probe requests. Successful probes
  close the circuit; failed probes reopen it.
```

As an initial example, I might use a failure ratio over a sampling window
with a minimum throughput, such as 50% failures over at least 20 eligible
requests in 30 seconds, followed by a 15–30 second break duration. Those are
illustrative values, not universal defaults. They must be tuned from provider
SLOs, normal traffic, and load tests. A breaker that opens after two ordinary
timeouts creates unnecessary failover; one that waits for thousands of failed
requests creates an outage amplifier.

The breaker should count systemic dependency failures and timeouts, not client
mistakes such as invalid JSON, unsupported models, or unauthorized callers.
429 handling needs separate thought: a short overload response may be
retryable, while an exhausted account quota should disable the route and alert
operations rather than repeatedly opening and probing it.

### Multiple application replicas

Each process may have a local breaker, but each Kubernetes or Docker replica
then sees only part of the failure rate. Local breakers are fast and simple,
but they do not by themselves protect a shared provider quota. I would combine
them with one of these approaches:

- a centralized provider-aware rate and concurrency limiter;
- a routing service that maintains provider health and quota state; or
- a shared admission controller in front of the workers.

Half-open probes should be single-flight per route so all replicas do not
probe an unhealthy provider at once. A distributed breaker is possible, but it
adds coordination latency and its own availability problem; I would usually
prefer local breakers plus centralized admission control.

### Fallback and degradation

When a circuit opens, the orchestrator tries at most one fallback route for a
request. It checks:

- model capability and structured-output compatibility;
- privacy and data-residency requirements;
- provider retention and training settings;
- expected latency and cost;
- tenant/provider contractual restrictions;
- whether the secondary route can safely perform the requested operation.

Fallback should not silently send sensitive resident information to an
unapproved provider. For some operations, a smaller approved model is an
acceptable degraded path. For others, the only safe fallback is a durable
queue and human review.

```text
Client
  -> WAF / API Gateway
  -> Application API
  -> LLM Orchestrator
       -> Provider A [circuit open]
       -> Provider B [compatible and healthy]
       -> Durable queue [delayed work]
       -> Controlled 429/503 [interactive failure]
```

Interactive requests should fail fast with a controlled response and, where
appropriate, a `Retry-After` value. A background summarization or enrichment
job can be persisted to Azure Service Bus, RabbitMQ, or another durable broker
and returned as `202 Accepted` with an operation ID. Queue consumers use
bounded retries, exponential backoff, visibility timeouts, and a dead-letter
queue. Queue age and maximum message age are monitored so delayed work does
not become silently stale.

Retries and failover must not create uncontrolled duplicate work. A caller
supplies an `Idempotency-Key`; the system stores it with a request hash,
operation state, and result reference. Reusing the key with the same request
returns the existing operation or result. Reusing it with a different request
returns a conflict. Records have a retention policy. This prevents duplicate
business processing when clients retry or brokers redeliver messages, although
a provider may still charge for a request that timed out after it was accepted
unless that provider offers its own idempotency mechanism.

### Resilience telemetry

I would measure and alert on provider, region, model, operation, and tenant:

- request, retry, timeout, 429, 5xx, and authentication-error rates;
- latency percentiles and token consumption;
- circuit state transitions and open duration;
- fallback and degraded-mode rates;
- outbound concurrency and rejected admissions;
- queue age, retry count, and dead-letter volume;
- provider cost and tenant budget consumption.

The system should expose a health/readiness view that explains whether a route
is healthy, rate-limited, circuit-open, credential-invalid, or disabled by
quota. It should not expose provider keys or sensitive prompts in that view.

## 2. API keys, secrets, rotation, and least privilege

Secrets should never be committed to source control, embedded in Docker images,
placed in frontend bundles, returned to clients, or written to logs. In
production I would use Azure Key Vault, AWS Secrets Manager, Google Secret
Manager, or HashiCorp Vault. For an Azure deployment, I would use Microsoft
Entra ID, a managed identity, and narrowly scoped Key Vault RBAC. Azure’s
security guidance recommends managed identities and RBAC to avoid hard-coded
credentials and reduce administrative exposure. See
[Azure Key Vault security guidance](https://learn.microsoft.com/en-us/azure/key-vault/general/secure-key-vault).

```text
Application / Worker
       |
       | Managed identity / Entra ID token
       v
  Secret manager
       |
       +--> LLM provider credential
       +--> database or broker credential, if still required
```

The application identity should read only the specific secret it needs. It
should not have permission to create, delete, list, or rotate arbitrary
secrets. A rotation identity can write or replace only the designated
credentials and should not be able to invoke the LLM service. Administrators
use just-in-time privileged access and audited approval for exceptional
changes.

### Build and CI/CD handling

Builds should not need production LLM keys. CI should authenticate to the
cloud using short-lived OIDC/workload-identity federation instead of a stored
long-lived cloud service-principal secret. The build identity can compile,
test, scan, and publish an artifact; it does not need to read production
secret values.

Production credentials are injected or referenced at deployment/runtime and
are never baked into source, Docker layers, frontend bundles, or build
artifacts. Unit tests use mocks. Integration tests use disposable sandbox
credentials stored in the CI secret manager, masked in logs, scoped to the
test environment, unavailable to untrusted pull requests, and revoked or
expired after use.

I would add secret detection to both developer and CI workflows using tools
such as GitHub Secret Scanning, Gitleaks, or TruffleHog. A detected credential
is treated as compromised: revoke it, investigate its use, remove it from
history where appropriate, and do not merely delete the visible string from
the latest commit.

The identities are separated by function:

| Identity | Minimum responsibility |
|---|---|
| Build identity | Build, test, scan, and publish artifacts |
| Deploy identity | Deploy infrastructure and configure secret references |
| Runtime API identity | Read only the API secrets required by that service |
| Runtime worker identity | Read only its provider and queue/database secrets |
| Rotation identity | Create or replace designated credentials, without inference permissions |
| Human administrator | Just-in-time, audited emergency access |

Environment variables are acceptable for local development or controlled
container injection, but I would not use them as the primary production secret
store. Local development should use a developer secret store such as
`.NET user-secrets`, a local vault, or an explicitly ignored `.env` file. No
local secret should be committed.

### Rotation

Where a provider supports two simultaneously valid credentials, I would use
staged rotation:

```text
1. Create a new credential in the provider.
2. Store it as a new version in the secret manager.
3. Reload or redeploy consumers without exposing the value.
4. Confirm traffic is succeeding with the new version.
5. Monitor for stale consumers and authentication failures.
6. Revoke the previous credential after the rollback window.
```

The application should refresh a versionless secret reference periodically or
on a secret-change event rather than loading a key once and retaining it
forever. Rotation needs expiration alerts, audit events, rollback, and a
defined response if only one provider key can be active at a time. Azure Key
Vault documents versioned secrets, near-expiry events, and automated rotation
patterns in its [rotation guidance](https://learn.microsoft.com/en-us/azure/key-vault/secrets/tutorial-rotation).

For cloud resources that support workload identity or Entra-based
authentication, I would eliminate a stored credential rather than rotate it.

## 3. Attack surfaces and mitigation priorities

The public API is only the first boundary. A representative deployment is:

```text
Internet
  -> WAF / edge protection
  -> API gateway / API Management
  -> BFF or application API
  -> LLM orchestrator
       -> provider APIs
       -> tool gateway
       -> databases and RAG indexes
       -> message broker
       -> model/adaptor registry
```

The edge and API gateway can provide JWT validation, rate limits, quotas,
request-size checks, schema checks, and observability. For example, Azure API
Management documents `validate-jwt`, rate-limiting, and `validate-content`
policies. [Azure API Management policy documentation](https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-policies)
The gateway is only a coarse boundary: it cannot replace application-level
tenant authorization or semantic authorization of a tool call.

### Highest-priority threats

#### 1. Broken authorization and tenant isolation — P0

Authentication is not enough. Every request must be authorized against the
tenant, resident, conversation, document, embedding, tool result, and
operation being accessed. A user from tenant A must never retrieve tenant B’s
records or RAG context. Authorization must be enforced server-side and again
at the data and tool boundaries.

#### 2. Credential and secret exposure — P0

LLM keys, database credentials, queue credentials, signing keys, CI tokens,
and deployment identities are high-value targets. Managed identity, narrow
RBAC, short-lived CI tokens, secret scanning, private networking where
appropriate, and redacted logs reduce the blast radius.

#### 3. Prompt injection and excessive agency — P0

The LLM is not an authorization boundary. User prompts, uploaded documents,
retrieved pages, incident reports, and tool responses are untrusted data. A
model may propose a tool call, but a deterministic tool gateway must validate
the caller, tool allow-list, tenant/resource authorization, argument schema,
sensitivity, approval requirement, and audit record before execution.

```text
LLM proposes tool call
  -> Tool gateway
       -> authenticated caller?
       -> authorized for this tenant and resource?
       -> allow-listed tool?
       -> valid and bounded arguments?
       -> human approval required?
       -> audit and idempotency checks
  -> internal service
```

No prompt or model output should be able to grant itself permissions.

#### 4. Sensitive-data disclosure and provider governance — P0/P1

Prompts, completions, embeddings, tool responses, and logs may contain
personal, medical, proprietary, or credential-like data. I would classify data,
minimize fields sent to the model, apply tenant and field-level filtering,
configure provider retention and training settings, restrict regions, encrypt
data in transit and at rest, and audit access. Fallback routing must apply the
same data-residency and provider-approval rules as the primary route.

#### 5. RAG, conversation, and training-data poisoning — P1

An attacker or mistaken workflow may plant instructions in a document, case
history, summary, feedback record, or policy index. Later retrieval could make
that content appear trusted. Policy sources need ownership, approval,
versioning, temporal validity, and provenance. Historical cases must be
filtered by review status and tenant. LoRA candidates require human validation,
data lineage, de-identification, and evaluation before training.

#### 6. Insecure output handling and model overreliance — P1

Model output is untrusted data. It must not be passed directly into SQL, shell
commands, HTML, code execution, or privileged APIs. Use schema validation,
encoding, allow-listed operations, deterministic policy checks, and human
approval for high-impact actions. A valid JSON response is not necessarily a
safe decision.

#### 7. Supply-chain and model-registry compromise — P1

Attackers may target packages, container images, CI actions, model weights,
LoRA adapters, prompt configurations, or third-party tools. I would pin and
scan dependencies, sign and verify artifacts, restrict registry write access,
record model/adaptor hashes and provenance, use isolated build runners, and
require evaluation gates before a model or adapter can reach production.

#### 8. Denial of service and cost exhaustion — P1

An attacker can stay under a simple requests-per-second limit while sending
large prompts to an expensive model. Enforce per-user and per-tenant request,
concurrency, input-token, output-token, maximum-prompt-size, and monetary
budgets. Combine inbound limits with outbound provider quotas and queue
backpressure.

#### 9. SSRF and unrestricted outbound access — P1

If an agent can fetch URLs or call external tools, use destination allow-lists,
DNS and IP validation, private-address blocking, egress controls, timeouts,
redirect limits, and response-size limits. The agent must not become a proxy
into internal metadata services or private infrastructure.

#### 10. Queue replay, duplicate execution, and logging leakage — P1/P2

Queue consumers need immutable operation IDs, idempotency, bounded retries,
visibility timeouts, poison-message handling, and dead-letter queues. Logs
should use structured redaction for prompts, completions, RAG context, tool
parameters, authorization headers, and secrets, with controlled retention,
encryption, and audited access.

### Mitigation order

My initial mitigation order would be:

```text
P0: Tenant/object authorization and tool authorization
P0: Secret isolation and CI/CD supply-chain security
P0: Sensitive-data egress and provider-retention controls
P1: Prompt injection, excessive agency, and output validation
P1: RAG/training-data poisoning and model supply chain
P1: Rate, token, concurrency, and cost controls
P2: SSRF, queue replay, logging, and operational hardening
```

WAF and API Management are valuable perimeter controls, but they do not fix
broken object-level authorization or unsafe internal tool permissions. The
architecture therefore uses defense in depth: the gateway protects the public
boundary, the application enforces domain authorization, the orchestrator
controls provider resilience and cost, deterministic tool gateways protect
actions, and identity/secret controls reduce compromise impact.

## Validation and operational tradeoffs

I would test this design with failure injection and security exercises:

- simulate provider 429s, 5xx responses, timeouts, quota exhaustion, and
  credential failures;
- verify retry budgets, total deadlines, breaker transitions, half-open
  single-flight probes, and fallback compatibility;
- test multiple replicas against a shared provider quota;
- rotate credentials during active traffic and verify rollback;
- scan source, artifacts, containers, dependencies, model files, and CI
  configuration for secrets and known vulnerabilities;
- test cross-tenant access, malicious RAG documents, prompt injection,
  unauthorized tool calls, SSRF, output-injection payloads, queue replay, and
  cost exhaustion;
- verify that logs, traces, dead-letter messages, and error responses do not
  disclose secrets or unnecessary resident data.

The tradeoff is complexity. A provider-agnostic orchestrator, multi-provider
fallback, centralized quotas, secret rotation, tool gateway, and evaluation
pipeline cost more than a direct SDK call. They are justified when the system
handles sensitive data, expensive model calls, external side effects, or
high-consequence decisions. For low-risk internal summarization, a smaller
resilience and security profile may be sufficient; the controls should be
proportional to data sensitivity and consequence.

The main principle is defense in depth: providers may fail, models may be
manipulated, credentials may leak, and queues may redeliver messages. No single
LLM, gateway, circuit breaker, or secret store should be trusted to provide
the entire reliability or security boundary.
