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

**DONE**
- Background generation retries transient faults (`HttpRequestException`, `TimeoutException`, `IOException`, `SocketException`, plus `RedisConnectionException` when Redis is enabled) up to `MaxAttempts` with exponential backoff, bounded by a `ProcessingTimeoutSeconds` deadline. Cancellation and deadline expiry are terminal and never retried.
- `DocumentGenerationFailed` is published once after the final attempt instead of on every failure, and the task only reaches `Failed` when no attempts remain.
- The FOP converter client runs through `AddStandardResilienceHandler` (per-attempt timeout, total budget, exponential retry, circuit breaker), configured via `DocumentProcessing:Fop`. The library's validation invariants are satisfied automatically, so a hand-edited configuration cannot break the first request.
- In-memory cache and deduplication stores gained TTL and capacity bounds (`DocumentProcessing:InMemory`) so they match the Redis providers instead of growing for the lifetime of the process.
- The retention job now also fails tasks abandoned in `Processing`, so a crashed worker cannot leave a fingerprint pinned to a task that will never finish.

**Still open**
- Redis, RabbitMQ, and MinIO clients themselves have no explicit retry/circuit-breaker policy; only failures surfacing through generation are retried.
- No bulkhead or rate limiting on generation concurrency.

**Improve**
- Add explicit resilience around the remaining external infrastructure clients.

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

**Current state**
- In-process engine tests generate inspectable text, XLSX, DOCX, and PDF output in `artifacts/test-results/<engine>/`.
- Text templates cover TXT, HTML, and Markdown; XLSX and DOCX template fixtures verify that JSON values replace their placeholders in Open XML output.
- The MiniPdf test asserts rendered XLSX cell values, including a MiniExcel-generated workbook.
- The FOP adapter has an isolated HTTP contract test; its real PDF output still requires a running external converter.
- An opt-in integration test uses `FOP_CONVERTER_BASE_URL` to render a Scriban XSL-FO/XML template with data and generate a real PDF artifact from the external converter.

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

**DONE**
- Added `miniexcel` engine for JSON-to-XLSX (Excel) generation.
- Added `miniword` engine for template-driven .docx generation using `{{tag}}` syntax.
- Added `minipdf` engine for .docx/.xlsx-to-PDF conversion.
- Added `fop` engine for FO/XSL-FO-to-PDF conversion through the external FOP Converter API.
- All engines use streaming approaches to minimize memory usage.
- Registered priorities: miniword=100, miniexcel=150, minipdf=250.

**Improve**
- Add more formats and engines, for example:
  - DOCX generation without template (programmatic);
  - HTML to PDF alternative engine;
  - image rendering.

  The FOP adapter uses synchronous HTTP conversion, now wrapped in a standard resilience handler
  (timeout, retry, circuit breaker) — see §3.4.

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

As of 2026-11-07, **Phase 1 is complete and Phase 2 is mostly complete**. See §11 for the task-level record.

**Already implemented (not from §10):**
- Clean Architecture scaffold (Domain, Application, Infrastructure, Presentation, Host, Client)
- 8 engine plugins (fake, scriban, dotliquid, questpdf, miniexcel, miniword, minipdf, fop)
- Template parser registry and compiled template cache (in-memory + Redis)
- MassTransit in-memory messaging with RabbitMQ transport option
- Redis/RabbitMQ/MinIO infrastructure adapters
- Health checks, correlation middleware, OpenTelemetry, Serilog
- NBomber performance baselines (15 profiles) — documented in `docs/performance-baseline.md`
- Unit + integration tests

**Phase 1 — complete:**
- Durable task persistence (EF Core + PostgreSQL, advisory-locked migrations at startup)
- Input validation and guardrails (payload, template, metadata, and format-name limits)
- Auth/authz (JWT bearer with role-based policies; mandatory signing key)
- Resilience policies (bounded retry with backoff, processing deadline, FOP timeout/retry/circuit-breaker)

**Phase 2 — mostly complete:**
- Dedicated worker host — done
- Artifact retention/cleanup, including abandoned-task reaping — done
- Broader integration testing — done for PostgreSQL, RabbitMQ, and JWT auth; Redis and MinIO remain, see §11 items 7 and 12
- Richer dashboards — not started

**Known gaps that block a production-readiness claim:**
- No automated test exercises the Redis or MinIO adapter code paths. PostgreSQL, RabbitMQ, and JWT authorization are now covered by Testcontainers-backed and host-level integration tests (see §11 item 11); Redis and MinIO are not.
- Four MiniExcel engine defects are pinned by characterization tests rather than fixed: culture-dependent number formatting on the template path, nested objects silently written as their CLR type name, and two unhandled exceptions on ragged or nested payloads. The silent-corruption one ships bad documents.
- The byte[] pipeline (payload → `MemoryStream` → `byte[]` → cache/artifact copies) contradicts the "streaming" claim in §7.4 and is the dominant memory cost under load.
- Health endpoints are anonymous and publish engine implementation class names.
- Compose sets `ASPNETCORE_ENVIRONMENT=Development` on the API, so the "production-like" stack serves OpenAPI, Scalar, and console telemetry exporters.

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

## 11. Concrete task record and next tasks

1. ~~Add `docs/performance-baseline.md` or extend performance docs with standard benchmark matrix.~~ **DONE**
2. ~~Implement durable task repository.~~ **DONE** — EF Core + PostgreSQL, provider switching via `DocumentProcessing:Persistence:Provider`
3. ~~Add request validation and payload/template size limits.~~ **DONE** — configurable limits on payload, template, metadata, and format names via endpoint filter
4. ~~Add authentication and authorization.~~ **DONE** — JWT bearer auth with role-based policies, disabled by default for local dev
5. ~~Create a dedicated worker host.~~ **DONE** — separate `GenerateDocService.Worker` project, MassTransit consumer runs independently of API
6. ~~Add retention and cleanup for stored artifacts.~~ **DONE** — configurable retention (30d completed, 7d failed), background cleanup job with `DeleteAsync` on artifact store
7. Expand integration tests for production-like providers. **Originally marked DONE in error.** The 16 tests that were added cover validation errors, edge cases, failed generation paths, and in-progress download behaviour, all against **in-memory** providers. Cost already incurred: with the whole suite green, a missing EF migration and a RabbitMQ queue/endpoint name mismatch made the Dockerised async pipeline completely non-functional, and only a live end-to-end run exposed them. **Since closed for PostgreSQL, RabbitMQ, and JWT authorization by item 11; Redis and MinIO remain uncovered.**
8. ~~Make generation failures retryable and add resilience policies.~~ **DONE** — see §3.4. Transient faults retry with backoff under a processing deadline, the FOP client gained a timeout/retry/circuit-breaker pipeline, and the in-memory cache and deduplication stores gained TTL and capacity bounds.
9. ~~Reap tasks abandoned in `Processing`.~~ **DONE** — `ArtifactRetentionCleaner` fails them after `StuckProcessingTaskSeconds`, and the policy is now unit-tested separately from its scheduler.
10. ~~Clear the NuGet vulnerability advisories.~~ **DONE** — OpenTelemetry 1.12.0 → 1.20.1, `Microsoft.AspNetCore.OpenApi` 10.0.7 → 10.0.14 (which lifts the transitive `Microsoft.OpenApi` off the advisory), and `MessagePack` pinned to 2.5.307 in the NBomber project. A forced restore now reports zero `NU1902`/`NU1903` warnings; it previously reported 46.
11. ~~Add Testcontainers-backed coverage for the PostgreSQL repository and migrations, a RabbitMQ round-trip, and JWT authorization.~~ **DONE** — `Category=RequiresDocker` tests start real `postgres:17` and `rabbitmq:3-management` containers, apply migrations through the same `ApplyDocumentProcessingMigrationsAsync` call both hosts use, round-trip task state through `PostgreSqlDocumentGenerationTaskRepository`, and complete an async generation over RabbitMQ to prove the receive endpoint matches `QueueName`. Nine host-level tests cover JWT auth (401/403/200, expired and wrongly-signed tokens, anonymous health, startup failure on a short signing key). Containers skip cleanly when Docker is unreachable.
12. ~~Add binary transport so the Office-file engines are callable over HTTP.~~ **DONE** — `payloadBase64` and `templateBase64` join the JSON contract as mutually exclusive alternatives to `payload` and `template`. The validator rejects both-set, neither-set, and malformed base64, and measures size limits against the *decoded* length so encoding cannot bypass them; it derives that length arithmetically so the body is decoded once, in the mapper. This makes `miniword`, `minipdf`, and the MiniExcel template path reachable for the first time. Previously the host decoded `payload` and `template` with `Encoding.UTF8`, so a `.docx` or `.xlsx` could not survive the round trip and the README claim that minipdf takes a base64-encoded document was false. The async path needed no change because `GenerateDocumentRequested` already carries `byte[]`. Eight integration tests cover the three engines over HTTP plus the four rejection cases, and the sample client grew from ten examples to thirteen.

**Next tasks**

13. Fix template-parser selection. `TemplateParserRegistry.Resolve` orders candidates alphabetically by parser name, so `dotliquid` wins every format both parsers claim (`txt`, `html`, `md`, `markdown`) and `ScribanDocumentGenerationEngine` then rejects its compiled template. `engine=scriban` with `templateFormat=html` returns HTTP 500 even though both engines advertise those formats in their own `DocumentEngine` attributes. Resolution must consider the requesting engine, not just the format string.
14. Add the same container coverage for the Redis cache/deduplication/compiled-template adapters and the MinIO artifact store — the last untested provider paths.
15. Fix or formally accept the four characterized MiniExcel defects; the nested-object one silently ships corrupt documents.
16. Stream artifacts end to end instead of copying through `byte[]` at every hop (§7.4 claims streaming today). Base64 transport adds roughly 33% to binary request bodies, which raises the cost of this further.
17. Decide whether health-endpoint detail and Scalar should be exposed outside Development, and stop shipping `ASPNETCORE_ENVIRONMENT=Development` in the compose stack.
18. Add a CI gate so NuGet advisories cannot silently return.
19. Correct the remaining `localhost:7001` references in README and `docs/performance-baseline.md`; the API actually binds `https://localhost:7025` and `http://localhost:5287`.

---

## 12. Final recommendation

Do not rewrite the solution.

The current architecture is good enough to evolve incrementally.
The highest-value path is controlled hardening of persistence, security, resilience, and operations while preserving the current plugin and modular monolith structure.
