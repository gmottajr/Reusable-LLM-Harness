# Residential Care CRM — AI Layer System Design

## Question

You are building the AI layer of a CRM and operating system for owners of
residential care facilities for the elderly. Describe, in writing, how you
would design:

1. A multi-agent swarm triggered by a new resident intake form submission,
   including an orchestrator agent and sub-agents for medical history,
   regulatory compliance, and family communication. What does each agent own,
   and how do they communicate?
2. A looping incident-reporting workflow that classifies incidents, routes
   them to the correct notification path, and includes a max-iteration guard
   with a human escalation path if the loop does not converge.

## Architectural decision: three cooperating planes

Residential care combines unstructured information, long-running workflows,
sensitive resident data, jurisdiction-dependent regulation, family
communication, and safety-critical incident handling. AI is useful for
ambiguity in intake forms and incident reports, but workflow control itself
should remain deterministic and bounded.

A conventional orchestrator-plus-agents design has a critical gap: safe
orchestration does not give agents current organizational knowledge, and a
human correction in one exceptional case does not automatically improve the
next comparable case. I address that gap with an **Adaptive Intelligence
Plane** that is a core part of the architecture rather than an optional
learning project.

The system has three cooperating planes:

- **Control Plane:** durable workflow execution, state transitions, safety
  rules, authorization, validation, retries, timeouts, human work, and final
  completion or escalation state.
- **Reasoning Plane:** bounded medical-history, compliance, communication,
  incident-classification, and exception-resolution agents.
- **Adaptive Intelligence Plane:** an authorization-aware retrieval gateway,
  versioned policy and case indexes, operational memory, validated human
  corrections, and governed LoRA adapters.

RAG and LoRA have different responsibilities. **RAG is the fast runtime loop:**
it supplies current, case-specific evidence without retraining the model.
**LoRA is the slower behavioral loop:** it converts repeated, human-validated
behavioral corrections into controlled model improvements. Neither is the
source of truth. Canonical CRM/clinical/identity systems, versioned policy,
deterministic rules, qualified humans, and explicit validation gates establish
truth and permission to act.

RAG is runtime-critical whenever a decision requires current policy, resident
facts, permissions, or notification rules. LoRA is architecture-critical for
controlled organizational learning, but remains replaceable and rollback-safe
at runtime. If an adapter is unavailable, the system can use an approved
baseline model; if required evidence or policy is unavailable, it waits or
escalates rather than guessing.

```mermaid
flowchart TB
    Intake[Resident Intake Submitted] --> IntakeWF[Intake Durable Workflow]
    Incident[Incident Submitted] --> IncidentWF[Incident Durable Workflow]

    subgraph CP[Control Plane]
        IntakeWF
        IncidentWF
        Rules[Deterministic Safety and Authorization Rules]
        Validator[Independent Validation Layer]
        Matrix[Versioned Notification Matrix]
        Human[Human Review and Escalation]
        Delivery[Idempotent Delivery Workflow]
    end

    subgraph RP[Reasoning Plane]
        Medical[Medical History Agent]
        Compliance[Regulatory Compliance Agent]
        Family[Family Communication Agent]
        Classifier[Incident Classifier]
        Exception[Exception Resolution Agent]
    end

    subgraph AIP[Adaptive Intelligence Plane]
        Gateway[Retrieval and Authorization Gateway]
        Policy[Versioned Policy Index]
        Cases[Validated Case Index]
        History[Current Workflow History]
        Adapter[Versioned LoRA Adapter]
        Memory[(Provenance-aware Operational Memory)]
        Miner[Learning Candidate Miner]
        Registry[Evaluated Adapter Registry]
    end

    subgraph OP[Audit and Observability Plane]
        Audit[(Append-only Audit and Domain Events)]
        OTel[OpenTelemetry Instrumentation and Collector]
        Router[Configurable Telemetry Router]
        Signals[Redacted Operational Signals]
        Sinks[Datadog | Sentry | Grafana | Local Files | Custom DB]
    end

    IntakeWF --> Medical
    IntakeWF --> Compliance
    IntakeWF --> Family
    IncidentWF --> Rules
    Rules --> Classifier
    Rules --> Human

    Gateway <--> Medical
    Gateway <--> Compliance
    Gateway <--> Family
    Gateway <--> Classifier
    Gateway <--> Exception
    Policy --> Gateway
    Cases --> Gateway
    History --> Gateway
    Adapter --> Medical
    Adapter --> Compliance
    Adapter --> Family
    Adapter --> Classifier
    Adapter --> Exception

    Medical --> Validator
    Compliance --> Validator
    Family --> Validator
    Classifier --> Validator
    Validator -->|Accepted| Matrix
    Validator -->|Rejected or ambiguous| Exception
    Matrix --> Delivery
    Delivery -->|Failure after bounded retries| Human

    IntakeWF --> Memory
    IncidentWF --> Memory
    Medical --> Memory
    Compliance --> Memory
    Family --> Memory
    Classifier --> Memory
    Validator --> Memory
    Human --> Memory
    Memory --> Gateway
    Memory --> Miner
    Miner --> Registry
    Registry --> Adapter

    IntakeWF --> Audit
    IncidentWF --> Audit
    Medical --> Audit
    Compliance --> Audit
    Family --> Audit
    Classifier --> Audit
    Validator --> Audit
    Human --> Audit
    IntakeWF --> OTel
    IncidentWF --> OTel
    Medical --> OTel
    Compliance --> OTel
    Family --> OTel
    Classifier --> OTel
    Validator --> OTel
    OTel --> Router
    Router --> Sinks
    OTel --> Signals
    Audit --> Memory
    Signals --> Memory
```

## Intake workflow and agent ownership

The durable orchestrator owns the workflow lifecycle and state transitions,
the immutable intake snapshot and version, task fan-out/fan-in and
dependencies, retries, timeouts, cancellation, human-review tasks, and final
completion or escalation state. It coordinates work but does not make
medical, clinical, legal, or regulatory judgments.

The specialized agents have narrow ownership:

| Component | Owns | Must not do |
|---|---|---|
| Orchestrator | Workflow state, dependencies, scheduling, retries, human tasks, final state | Make clinical or legal judgments |
| Medical History Agent | Extracted reported facts; normalized medications, allergies, conditions; contradictions; missing information; risk flags for qualified review | Diagnose, prescribe, or turn uncertain input into clinical fact |
| Regulatory Compliance Agent | Cited obligations, required assessments, consent/documentation checks, jurisdiction- and facility-specific workflow recommendations | Make final legal or regulatory determinations from model memory |
| Family Communication Agent | Drafts based on approved facts, authorized recipients, templates, channel and language preferences | Decide disclosure permission or send unapproved sensitive content |
| Validator and rules | Evidence, authorization, policy, schema, severity, and route checks | Treat an agent’s confidence as proof |
| Notification workflow | Idempotent delivery, acknowledgement tracking, retries, dead-letter handling | Reclassify an already-converged incident |

The workflow begins by validating the form schema, deduplicating the
submission, checking authorization, and persisting an immutable intake
snapshot. The Adaptive Intelligence Plane then performs authorized retrieval:
canonical CRM or clinical lookups for resident facts and permissions, plus
hybrid search over current policy, approved templates, and validated case
history. RAG is a retrieval layer over those sources; it is not a replacement
for the authoritative systems.

The orchestrator fans out bounded tasks to the three agents. Independent
tasks can run concurrently, while dependencies remain explicit. For example,
the communication agent may draft a message concurrently, but it cannot send
until the facts are approved, recipient authorization is confirmed, and any
required clinician or authorized-human review has completed.

Agents communicate through typed commands and immutable results/events, not
unrestricted agent-to-agent conversation or shared mutable state. A result
contract should contain enough information for validation and reconstruction:

```text
AgentResult {
  tenant_id,
  case_id,
  agent_run_id,
  input_snapshot_version,
  findings,
  evidence_refs,
  retrieved_context_ids,
  policy_versions,
  missing_information,
  confidence,
  evidence_fingerprint,
  recommended_actions,
  needs_human_review,
  model_version,
  adapter_version,
  prompt_version,
  output_schema_version
}
```

Agents may read shared case projections, but they return immutable findings and
never overwrite another agent’s namespace. Write ownership remains explicit,
for example `medical.*`, `compliance.*`, `communications.*`, and
`workflow.*`. An append-only event stream records the decision, evidence,
policy and model versions, retrieved context, human overrides, retries,
notifications, and causation/correlation IDs.

The orchestrator fans in the results, detects contradictions and missing
dependencies, invokes validators, and either advances the workflow, requests
more information, creates a human-review task, or escalates. Long-running
steps are durable states, so the system can wait hours or days for documents
or human decisions without keeping an agent execution alive.

## Adaptive Intelligence Plane

The retrieval gateway combines canonical lookups with controlled hybrid
retrieval. Every retrieval operation applies tenant, facility, jurisdiction,
authorization, temporal-validity, and data-minimization filters.

- **Policy retrieval — “What should we do?”** Current regulations, approved
  facility procedures, clinical/operational policies, notification rules, and
  their versions. The policy source is authoritative; the RAG index is a
  derived, rebuildable retrieval projection.
- **Case retrieval — “What happened in comparable cases?”** Human-reviewed
  resolutions, overrides, exception paths, and validated precedent. Unreviewed
  AI output is not trusted precedent.
- **Workflow retrieval — “What has happened in this case?”** The current
  case’s decisions, validation failures, retrieved evidence, information
  requests, retries, and human interventions.

Current policy outranks historical precedent; human-reviewed precedent
outranks unreviewed AI output; and agent speculation is not precedent. If
required policy or authorization evidence cannot be retrieved, the decision
does not silently continue with model memory.

Human corrections are immediately useful through the fast RAG loop:

```text
case decision
  -> validator or human correction
  -> provenance-aware operational memory
  -> validated case retrieval for the next comparable case
```

The slow LoRA loop addresses a different problem. Repeated validated
corrections can improve stable behaviors such as structured extraction,
missing-evidence detection, calibrated abstention, incident classification,
and valid output formatting. Volatile resident facts, current policy,
permissions, and notification rules never belong in adapter weights.

Production output is only a training candidate. Human review, deterministic
ground truth, or an externally verified outcome must validate it before it
enters a versioned training set. Candidate data should be de-identified or
minimized, tenant-isolated, and filtered for stale policy. New adapters are
evaluated against the current baseline on frozen and recent hard cases, then
shadowed, canaried, and promoted only if safety and quality gates pass. Every
adapter records lineage to its base model, dataset, policy assumptions,
evaluation results, and deployment approval.

This is the symbiotic relationship:

```text
RAG supplies current evidence immediately.
Human validation supplies trustworthy corrections.
RAG makes the correction available to the next case.
Repeated corrections become LoRA training candidates.
LoRA improves stable future behavior after evaluation.
```

## Incident classification, notification, and bounded convergence

Incident processing receives less agent autonomy than intake. Deterministic
safety rules run before normal AI classification and bypass the loop for
configured high-consequence conditions such as immediate danger, serious
injury, hospitalization, suspected abuse, missing residents, medication
overdose/error, or death. The exact conditions and escalation obligations are
facility- and jurisdiction-specific.

For incidents that pass the immediate safety check, the classifier proposes a
structured category, severity, evidence set, confidence, and recommended
notification path. The independent validator checks the proposal against the
current incident taxonomy, required evidence, authorization, and retrieved
policy. Independence should come from deterministic rules or a separately
implemented validation path with different failure modes, not merely another
call to the same model and prompt.

Notification routing is deterministic after validation. A versioned,
jurisdiction-specific notification matrix maps incident category, severity,
jurisdiction, facility policy, resident authorization, required
acknowledgement, delivery order, and escalation timing to configured
recipients and channels. Depending on applicable policy and facts, recipients
may include on-duty staff, a clinical lead, an administrator, a
family/authorized representative, a physician or pharmacy, emergency
services, or a regulator. The architecture does not assume universal legal
obligations; the current policy matrix determines the path.

Convergence is explicit:

> **Converged** means the classification is accepted by independent
> validation, required evidence is present, and the notification route is
> deterministic under the current policy matrix.

The workflow persists `attempt_count`, the previous and current
`evidence_fingerprint`, validator rejection reasons, policy versions, and
deadlines as durable state. A fingerprint represents the material evidence
and rejection context used for an attempt; it is compared with the previous
attempt, not merely checked for existence.

```text
attempt_count += 1

if classification_accepted
   and required_evidence_present
   and route_is_deterministic:
    CONVERGED

if high_risk or workflow_timeout:
    persist_context()
    ESCALATE_TO_HUMAN

if attempt_count >= MAX_ATTEMPTS:
    persist_context()
    ESCALATE_TO_HUMAN

if attempt_count > 1 and current_fingerprint == previous_fingerprint:
    persist_context()
    ESCALATE_TO_HUMAN

otherwise:
    request_new_information_or_run_bounded_exception_resolution()
```

The Exception Resolution Agent does not simply ask the same model to “think
harder.” It identifies why validation failed, retrieves current policy and
validated comparable cases, requests specific missing information, or
escalates when there is no safe new evidence. High-risk uncertainty
short-circuits the loop instead of waiting for the iteration budget.

Human escalation is a resumable workflow state. The reviewer receives the
original report, extracted facts, proposed classifications, policy citations,
validator rejection reasons, attempt history, evidence-fingerprint changes,
and urgency/SLA. The reviewer can correct the classification, request
information, approve routing, or explicitly override it. The decision is
persisted with provenance and the workflow resumes from that state; it never
silently closes because automation failed.

Reasoning retries and notification-delivery retries are separate loops. Once
classification has converged, a delivery failure does not rerun
classification. Delivery commands use idempotency keys, acknowledgement
tracking, bounded backoff, and a dead-letter path. If delivery cannot be
confirmed, operations or a human is alerted without corrupting or replaying
the reasoning state.

## Observability, audit trail, and operational memory

Observability is a first-class, configurable plane rather than an afterthought.
The system emits structured logs, distributed traces, metrics, provider
responses, retry and circuit-breaker transitions, queue age, token/cost data,
retrieval results, agent runs, validation failures, human overrides, and
notification outcomes. I would instrument the .NET services with `ILogger`,
`Activity`/`ActivitySource`, and `Meter`, export through
[OpenTelemetry](https://opentelemetry.io/), and route through an OTLP-capable
collector or an application-level `IObservabilitySink` abstraction. Deployment
configuration can then select one or more sinks—Datadog, Sentry, Grafana
(for example, Loki/Tempo/Prometheus-compatible endpoints), another OTLP
provider, local JSON-lines files for development, or custom append-only audit
tables in the application database—without changing agent or workflow code.
Sampling, retention, redaction, tenant routing, and dual-write/failover
behavior should be configurable per environment and event class. Local files
are useful for development and emergency diagnostics, but are not the durable
production audit record by themselves.

The audit trail and observability telemetry serve related but different roles.
An append-only audit/domain-event store is the authoritative record of what the
system decided and did: workflow transitions, agent inputs and outputs by
reference, evidence references, policy/model/prompt/adapter versions, human
decisions, notification commands, and causation/correlation IDs. Operational
telemetry explains how the system behaved: latency, exceptions, provider
status, retries, circuit state, retrieval failures, queue pressure, and
resource consumption. Telemetry may be sampled or delayed and therefore is not
authoritative domain truth.

Both streams feed a controlled projection pipeline. Audit events and validated
human outcomes become provenance-aware workflow and case projections for RAG.
Redacted observability signals can also help the Exception Resolution Agent
detect repeated no-progress loops, stale policy retrieval, provider outages,
or abnormal model behavior. Raw logs, traces, prompts, and unreviewed telemetry
are never inserted directly into Case RAG or treated as precedent. Before any
observability-derived signal enters operational memory, the pipeline applies
tenant and authorization filters, removes secrets and unnecessary resident
data, records provenance and retention metadata, and requires validation when
the signal could influence a consequential decision.

Every event should carry a workflow/case/resident/incident identifier where
appropriate, tenant and facility scope, event type, timestamp, schema version,
severity, correlation ID, causation ID, producer version, and sensitivity
classification. Access to audit tables, telemetry providers, local files, and
RAG projections is separately permissioned and audited. This makes
observability useful for both live operations and institutional learning while
preventing the system from converting diagnostic noise into trusted knowledge.

## Reliability, security, and validation

The workflow uses a transactional outbox, idempotent consumers, optimistic
concurrency/versioning, durable recovery, circuit breakers, bounded retries,
timeouts, bulkheads, and dead-letter handling. External notification commands
are idempotent and carry a stable key derived from the incident, notification
type, recipient, and policy decision.

Resident data is protected through least-privilege tool access, tenant
isolation, encryption, retention and deletion controls, data minimization,
recipient authorization, and auditable access. Intake and incident text is
untrusted input: prompt-injection attempts are delimited as data, tool access
is allowlisted, and model output is schema-validated before it can affect
state or external systems.

Evaluation includes de-identified or synthetic intake and incident cases,
missing information, conflicting medical data, policy differences, high-risk
conditions, prompt injection, stale policy, unauthorized disclosure, and
misleading historical precedent. Useful measures include classification and
route correctness, severe-incident false-negative rate, unnecessary human
escalation rate, mean iterations to convergence, no-progress termination,
policy/provenance coverage, duplicate external actions, recovery time, and
latency.

Example acceptance criteria are:

- Every consequential automated decision has reconstructable evidence and
  provenance.
- Every external notification command is idempotent and duplicate-safe.
- Every configured high-severity uncertainty path reaches mandatory
  escalation.
- No workflow remains silently unresolved beyond its configured timeout/SLA.
- No unreviewed AI decision is treated as policy or trusted precedent.
- Policy conclusions identify the policy and rule version used at decision
  time.
- Human overrides remain structured correction events.
- New adapters do not regress severe-incident detection, abstention,
  privacy, policy grounding, or structured-output validity.

The design is intentionally hybrid: agents reason, workflows control, current
evidence comes through authorized retrieval, stable behavior improves through
governed LoRA, and humans or deterministic evidence establish truth. When the
system cannot know safely, it fails visibly and escalates.
