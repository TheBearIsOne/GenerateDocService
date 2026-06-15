# GenerateDocService Improvement Plan

## 1. Goal

This document describes how the current project can be improved without breaking the current modular monolith direction.

The emphasis is on practical, staged improvements.

---

## 2. Priority model

- **P0**: important for reliability or safe production usage
- **P1**: strong architectural or operational improvement
- **P2**: useful enhancement
- **P3**: optional or later-scale optimization

---

## 3. Recommended improvement backlog

## P0 — Reliability and production readiness

### 3.1 Durable task persistence

**Current state**
- Task repository is abstracted, but the active implementation path is lightweight.

**Improve**
- Add durable persistence for task records and task history.
- Prefer PostgreSQL or SQL Server for task state, audit timestamps, and querying.

**Why**
- Async processing needs recoverability after restart.
- Durable status queries are critical for clients.

**How**
- Introduce an infrastructure repository implementation using EF Core or Dapper.
- Persist task state transitions.
- Add migration scripts.

### 3.2 Security and access control

**Current state**
- API endpoints are open.

**Improve**
- Add authentication and authorization.
- At minimum support service-to-service auth using JWT bearer tokens.

**Why**
- Document generation often processes sensitive data.

**How**
- Add ASP.NET Core authentication/authorization.
- Protect generation, task, and download endpoints.
- Define roles/scopes for submit, read-status, download, and admin operations.

### 3.3 Request validation and guardrails

**Current state**
- API contracts are simple and flexible.

**Improve**
- Add validation for:
  - payload size;
  - template size;
  - allowed formats;
  - metadata limits;
  - request timeouts/cancellation;
  - malformed input behavior.

**Why**
- Prevent resource abuse and accidental overload.

**How**
- Add request validators in presentation/application layer.
- Return clear 400 responses.
- Add max size configuration.

### 3.4 Resilience policies for external dependencies

**Current state**
- Redis, RabbitMQ, and MinIO are configurable, but retry/circuit behavior is implicit.

**Improve**
- Add explicit resilience around external infrastructure.

**Why**
- High-load services require controlled failure behavior.

**How**
- Add retry, timeout, and circuit-breaker policies where appropriate.
- Make policy configuration explicit.

---

## P1 — Architecture hardening

### 4.1 Dedicated worker host

**Current state**
- API host also handles background processing.

**Improve**
- Split background generation into a dedicated worker process while keeping the same application core.

**Why**
- Separate scaling profile for HTTP ingress and heavy background work.

**How**
- Create `src/Host/GenerateDocService.Worker`.
- Reuse the same application and infrastructure services.
- Disable HTTP endpoints in the worker.

### 4.2 Formal engine capability model

**Current state**
- Capability matching is based on `CanHandle` and metadata.

**Improve**
- Add explicit compatibility/cost metadata.

**Why**
- Future routing may need more than priority.

**How**
- Extend engine descriptor with fields such as:
  - estimated cost;
  - latency class;
  - resource intensity;
  - synchronous support;
  - asynchronous support.

### 4.3 Artifact lifecycle management

**Current state**
- Artifacts are stored and downloadable, but lifecycle governance is limited.

**Improve**
- Add retention, expiration, and cleanup jobs.

**Why**
- Artifact storage grows indefinitely otherwise.

**How**
- Add expiration metadata;
- add cleanup background job;
- optionally add legal hold/archive strategies.

### 4.4 Stronger domain model for task lifecycle

**Current state**
- Task states exist, but the workflow is application-driven.

**Improve**
- Tighten lifecycle invariants around retries, ownership, and transitions.

**Why**
- This reduces invalid state changes as the system grows.

**How**
- Enforce richer domain transition methods;
- add retry counters and failure categories.

---

## P1 — Observability and operations

### 5.1 Better operational dashboards

**Current state**
- Metrics exist, but dashboards are not described.

**Improve**
- Add Grafana or Azure Monitor workbook dashboards.

**How**
- Track:
  - request rate;
  - success/failure ratio;
  - duration percentiles by engine;
  - queue depth;
  - cache hit ratio;
  - download volume.

### 5.2 Structured event taxonomy

**Current state**
- Domain events exist for generated/failed.

**Improve**
- Formalize event names and schema versioning.

**Why**
- Safer future integration with analytics and external consumers.

### 5.3 Auditability

**Improve**
- Add audit entries for generation requests, task completion, failure, and artifact download.

---

## P2 — Developer experience and testing

### 6.1 Expand integration coverage

**Improve**
- Add tests for:
  - failed generation paths;
  - invalid request validation;
  - Redis/MinIO-enabled integration scenarios;
  - RabbitMQ transport scenarios;
  - download access control once auth exists.

### 6.2 Add contract tests for engines

**Improve**
- Create a reusable conformance suite that every engine must pass.

**Why**
- New engines can be added safely.

### 6.3 Add parser/cache benchmarks

**Current state**
- NBomber baselines exist.

**Improve**
- Add lower-level benchmarks for parser compilation and cache hit/miss behavior.

**How**
- Keep NBomber for end-to-end.
- Add focused microbenchmarks separately if needed.

### 6.4 Better local bootstrap

**Improve**
- Add a single developer bootstrap guide.
- Add convenience commands/scripts for:
  - run API;
  - run docker infra;
  - run sample client;
  - run perf baselines.

---

## P2 — Product capabilities

### 7.1 Template and document management

**Improve**
- Add first-class template storage and versioning.

**Why**
- Today templates are request-bound; future systems often need managed templates.

### 7.2 Batch generation

**Improve**
- Support one request generating multiple documents.

### 7.3 Callback/webhook completion model

**Improve**
- Add callback/webhook support for async completion instead of polling only.

### 7.4 More engines and converters

**Improve**
- Add more formats and engines, for example:
  - DOCX generation;
  - HTML to PDF alternative engine;
  - image rendering;
  - spreadsheet generation.

---

## P3 — Advanced scalability

### 8.1 Multi-tenant isolation

**Improve**
- Add tenant-aware routing, quotas, artifact isolation, and cache partitioning.

### 8.2 Engine isolation/sandboxing

**Improve**
- Run risky or heavy converters in isolated worker processes or containers.

### 8.3 Horizontal partitioning

**Improve**
- Partition task and artifact domains if throughput grows substantially.

### 8.4 Intelligent routing

**Improve**
- Use richer routing policies than simple priority:
  - latency-aware routing;
  - load-aware routing;
  - fallback engine routing.

---

## 9. Current project position

As of 2026-06-14, the project is **before Phase 1**. All items in Section 10 are pending or in progress.

**Already implemented (not from §10):**
- Clean Architecture scaffold (Domain, Application, Infrastructure, Presentation, Host, Client)
- 4 engine plugins (fake, scriban, dotliquid, questpdf)
- Template parser registry and compiled template cache (in-memory + Redis)
- MassTransit in-memory messaging with RabbitMQ transport option
- Redis/RabbitMQ/MinIO infrastructure adapters
- Health checks, correlation middleware, OpenTelemetry, Serilog
- NBomber performance baselines (15 profiles) — documented in `docs/performance-baseline.md`
- Unit + integration tests

**Phase 1 tasks — pending:**
- Durable task persistence (PostgreSQL/SQL Server via EF Core or Dapper)
- Input validation and guardrails (payload size, template size, format limits)
- Auth/authz (JWT bearer, role-based authorization)
- Resilience policies (retry, timeout, circuit-breaker for Redis, RabbitMQ, MinIO)

---

## 10. Suggested phased roadmap

## Phase 1

- durable task persistence;
- input validation and guardrails;
- auth/authz;
- resilience policies.

## Phase 2

- dedicated worker host;
- artifact retention/cleanup;
- richer dashboards;
- broader integration testing.

## Phase 3

- template management;
- callback/webhook completion;
- engine conformance suite;
- more converters.

## Phase 4

- tenant isolation;
- engine sandboxing;
- intelligent routing and scaling policies.

---

## 11. Concrete immediate next tasks

1. ~~Add `docs/performance-baseline.md` or extend performance docs with standard benchmark matrix.~~ **DONE**
2. ~~Implement durable task repository.~~ **DONE** — EF Core + PostgreSQL, provider switching via `DocumentProcessing:Persistence:Provider`
3. Add request validation and payload/template size limits.
4. Add authentication and authorization.
5. Create a dedicated worker host.
6. Add retention and cleanup for stored artifacts.
7. Expand integration tests for production-like providers.

---

## 12. Final recommendation

Do not rewrite the solution.

The current architecture is good enough to evolve incrementally.
The highest-value path is controlled hardening of persistence, security, resilience, and operations while preserving the current plugin and modular monolith structure.
