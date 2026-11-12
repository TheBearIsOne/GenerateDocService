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

**DONE** — EF Core over PostgreSQL, selected by `DocumentProcessing:Persistence:Provider`. Task state and the pending command payload are committed atomically, an outbox dispatcher claims rows under a lease, and both hosts apply migrations at startup behind a PostgreSQL advisory lock so concurrent starts cannot race on DDL. Covered by `Category=RequiresDocker` tests that run the same `ApplyDocumentProcessingMigrationsAsync` call the hosts use. See §11 item 2.

### 3.2 Security and access control

**DONE** — JWT bearer authentication with role-based policies (`DocumentSubmit`, `DocumentRead`, `DocumentDownload`, `DocumentAdmin`). Disabled by default for local `dotnet run`, enabled in the compose stack. The signing key has **no committed default**: the host fails fast at startup when auth is enabled and the key is empty or shorter than 32 characters, so a missing secret cannot degrade into a well-known key. Health probes stay anonymous so orchestrators keep working. Nine host-level tests cover 401/403/200, expired and wrongly-signed tokens, and the startup failure. See §11 item 4.

**Still open**
- No secret store or rotation story; the key arrives through configuration or `GDS_AUTH_SIGNING_KEY`.
- No per-tenant scoping — roles are global.

### 3.3 Request validation and guardrails

**DONE** — `GenerateDocumentRequestValidator`, applied through `ValidationRequestFilter` on the generation endpoints, enforces configurable limits on payload size (10 MB), template size (5 MB), metadata entries and key/value lengths, format and engine name lengths, and request ID length. Base64 fields are validated strictly (standard alphabet, length a multiple of four, at most two trailing `=`, no embedded whitespace) and size limits are measured against the **decoded** length, derived arithmetically so the body is decoded once, in the mapper — encoding cannot be used to exceed a limit. `payload`/`payloadBase64` and `template`/`templateBase64` are mutually exclusive. Violations return `400` with field-level detail. Configure via `DocumentProcessing:Validation`. See §11 items 3 and 12.

**Still open**
- Limits are per-request guardrails, not a quota or admission-control model, so aggregate memory pressure under concurrency is still unbounded.
- `MaxProcessingTimeout` in early configuration samples did not match the bound `MaxProcessingTimeoutMinutes` in either name or type, so the setting was silently dead; the current `DocumentProcessing:Resilience:ProcessingTimeoutSeconds` is bound and tested. Keep an eye on the same class of drift when adding settings.

### 3.4 Resilience policies for external dependencies

**DONE**
- Background generation retries transient faults (`HttpRequestException`, `TimeoutException`, `IOException`, `SocketException`, plus `RedisConnectionException` when Redis is enabled) up to `MaxAttempts` with exponential backoff, bounded by a `ProcessingTimeoutSeconds` deadline. Cancellation and deadline expiry are terminal and never retried.
- An engine can veto retry by throwing `NonRetryableGenerationException`. `TransientFailureClassifier` checks for it before any transient type at every level of the inner-exception chain, so the opt-out always wins. The `api` engine uses it: a configured target is an unknown third party whose endpoints may not be idempotent, and the request may already have been received before the failure surfaced, so an automatic retry could duplicate a side effect. `fop` keeps retrying because its converter is idempotent.
- `DocumentGenerationFailed` is published once after the final attempt instead of on every failure, and the task only reaches `Failed` when no attempts remain. `RecordFailed` is likewise called once, so retry pressure shows up in `document_generation_duration_ms` rather than in the failure counter.
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

**DONE** — `src/Host/GenerateDocService.Worker` runs the MassTransit consumer independently of HTTP ingress, reuses the same Application and Infrastructure layers, and publishes no HTTP surface. API and Worker scale separately, and compose deploys both. See §11 item 5.

**Still open**
- No per-engine worker pool, so a slow heavyweight converter shares concurrency with cheap text generation. That is the bulkhead gap from §3.4.

### 4.2 Formal engine capability model

**Current state**
- Capability matching is based on `CanHandle` and the `[DocumentEngine]` metadata, resolved by priority descending then name ascending.
- The `api` engine already strains this model: its capabilities come from configuration rather than metadata, so it registers with **empty** format arrays and relies entirely on `CanHandle` consulting the configured targets. Its priority of 10 keeps purpose-built engines ahead of it, which is the only thing preventing a generic target from shadowing `fop` on `xsl-fo -> pdf`.

**Improve**
- Add explicit compatibility/cost metadata.

**Why**
- Future routing may need more than priority. A configuration-driven engine cannot express its capabilities statically, and "lowest priority so it loses" is a convention rather than a model.

**How**
- Extend engine descriptor with fields such as:
  - estimated cost;
  - latency class;
  - resource intensity;
  - synchronous support;
  - asynchronous support;
  - whether capabilities are static metadata or resolved at runtime.

### 4.3 Artifact lifecycle management

**DONE** — `ArtifactRetentionCleaner` deletes completed tasks and their artifacts after `RetentionDays` (30) and failed task records after `FailedTaskRetentionDays` (7), and moves tasks still `Processing` after `StuckProcessingTaskSeconds` (1800) to `Failed`. `ArtifactCleanupBackgroundService` is only its scheduler; the policy is a separate class so it is unit-testable without waiting an interval. Configure via `DocumentProcessing:Retention`. See §11 items 6 and 9.

**Still open**
- No legal hold or archive strategy; retention is a single window per terminal state.
- Cleanup deletes the task record and the artifact, but there is no audit trail of what was removed.

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

**DONE for every listed item** — failed generation paths, invalid request validation, Redis/MinIO-enabled scenarios, RabbitMQ transport, and download access control are all covered. Redis and MinIO use real containers with the infrastructure graph built from configuration, so the adapters are resolved exactly as the hosts resolve them. See §11 items 7, 11 and 14.

**Still open**
- No test drives one request through the Docker compose stack with all four external providers selected at once. The container tests each exercise one provider against an otherwise in-memory graph, which is not the shipped configuration. See §11 item 21.
- The `api` engine is covered against `MockApiService`, a real Kestrel host on an ephemeral loopback port. Its `fopconverter`-backed example has only been proven to resolve the target, not to produce a PDF, because that stack was not running when it was added.

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

**DONE** — BenchmarkDotNet 0.15.8 is hosted inside `GenerateDocService.SampleClient`, run with `-- benchmark` (or the `b` menu command) under a Release build. Three classes in `Benchmarks/InProcessBenchmarks.cs`: `EngineGenerationBenchmarks` (scriban, dotliquid, questpdf, miniexcel table and template paths, miniword), `TemplateParserCacheBenchmarks` (compile on cache miss versus warm `InMemoryCompiledTemplateCache` hit, for both parsers), and `RequestPipelineBenchmarks` (fingerprinting at 1 KB and 1 MB, base64 encode/decode of 1 MB). Inputs are built in `[GlobalSetup]` so measured methods allocate only what the engine does. `BenchmarkDotNet.Artifacts/` is gitignored.

**Current state**
- NBomber baselines exist for end-to-end HTTP load.
- In-process microbenchmarks now exist alongside them.

**Still open**
- No benchmark covers `GenerateDocumentRequestValidator`, which lives in the API host; referencing that project from the sample client would pull the whole web host into a demo executable. Covering it needs either a shared validation library or a benchmark project outside `src/Clients`.
- Results are not captured as a committed baseline, so there is nothing to regress against yet.

**How**
- Keep NBomber for end-to-end.
- Keep the BenchmarkDotNet classes for component-level comparisons.

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
- Added `api` engine: a configurable generalisation of `fop` that posts the payload to a named external HTTP target and returns the response as the document. Targets and their credentials live in configuration, never in the request, so a caller can select a pre-approved target but cannot supply a URL or a secret. Supports `None`, `Basic`, `Bearer` and `OAuth2ClientCredentials` (with cached, skew-aware token refresh and a single in-flight fetch). Covered by a mock HTTP service exercising every scheme plus the unauthenticated path.
- Registered priorities: api=10, miniword=100, miniexcel=150, minipdf=250, fop=300.

**Correction:** the claim that "all engines use streaming approaches to minimize memory usage" was never accurate and still is not — every engine materialises its whole document as a `byte[]`. See item 16.

**Improve**
- Add more formats and engines, for example:
  - DOCX generation without template (programmatic);
  - HTML to PDF alternative engine;
  - image rendering.

  The FOP adapter uses synchronous HTTP conversion, now wrapped in a standard resilience handler
  (timeout, retry, circuit breaker) — see §3.4. The `api` engine deliberately has **no** retry,
  because a configured target is an unknown third party whose endpoints may not be idempotent; it
  applies a per-target timeout instead.

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
- Clean Architecture structure (Domain, Application, Infrastructure, Presentation, Host, Client)
- 9 engine plugins (fake, scriban, dotliquid, questpdf, miniexcel, miniword, minipdf, fop, api)
- Template parser registry with engine-preferred resolution, and compiled template cache (in-memory + Redis)
- MassTransit in-memory messaging with RabbitMQ transport option
- Redis/RabbitMQ/MinIO infrastructure adapters
- Binary (base64) payload and template transport
- Health checks, correlation middleware, OpenTelemetry, Serilog
- NBomber performance baselines (13 profiles) — documented in `docs/performance-baseline.md`
- In-process BenchmarkDotNet microbenchmarks hosted in the sample client
- A CI workflow that fails the build on NuGet advisories
- Unit + integration tests

**Phase 1 — complete:**
- Durable task persistence (EF Core + PostgreSQL, advisory-locked migrations at startup)
- Input validation and guardrails (payload, template, metadata, and format-name limits)
- Auth/authz (JWT bearer with role-based policies; mandatory signing key)
- Resilience policies (bounded retry with backoff, processing deadline, FOP timeout/retry/circuit-breaker)

**Phase 2 — mostly complete:**
- Dedicated worker host — done
- Artifact retention/cleanup, including abandoned-task reaping — done
- Broader integration testing — done for PostgreSQL, RabbitMQ, JWT auth, Redis and MinIO; see §11 items 11 and 14
- Richer dashboards — not started

**Known gaps that block a production-readiness claim:**
- Generation is still not streamed end to end. Both HTTP response paths now hand their stream to `Results.File` instead of copying to a `byte[]`, but every engine still materialises its whole document in memory and the artifact store and cache each hold a full copy. Base64 request transport adds roughly 33% on top for binary input.
- One MiniExcel defect is deliberately accepted rather than fixed: a template placeholder bound to a nested object or array still raises an unhandled `IndexOutOfRangeException` from MiniExcel's renderer. Fixing it properly means telling `{{key}}` apart from `{{list.field}}`, which requires parsing the workbook's template tokens. The other three are fixed — see §11 item 15.
- Compose still exports OpenTelemetry traces and metrics to the console, which is developer-oriented output for a stack that now runs as Production.
- Redis and MinIO adapters are covered at the adapter level only. No test drives a full request through the Docker compose stack with all four external providers selected at once.

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
13. ~~Fix template-parser selection.~~ **DONE** — `ITemplateParserRegistry.Resolve` takes an optional `preferredParserName`, and each engine now passes its own name, so a contested format resolves to the parser that can actually use the result. `engine=scriban` with `templateFormat=html` returned HTTP 500 before this and renders correctly now. Six HTTP-level combinations and the registry tie-break rules are covered by tests; without a preference the old alphabetical order is retained and pinned so changing it is deliberate.
14. ~~Add container coverage for the Redis and MinIO adapters.~~ **DONE** — real `redis:7` and `minio/minio` containers, with the infrastructure graph built from configuration so the adapters are resolved exactly as the hosts resolve them. Covers deduplication ownership and TTL, generated-document round trip, Scriban compiled-template round trip verified by *rendering* the re-parsed template, MinIO bucket creation plus artifact round trip and deletion, and malformed storage paths. Also pins that `RedisCompiledTemplateCache` silently stores nothing for DotLiquid compilations, so those re-parse on every request under the Redis provider.
15. ~~Fix or formally accept the four characterized MiniExcel defects.~~ **Three fixed, one accepted.** Culture: the engine now passes an `OpenXmlConfiguration` with `Culture = InvariantCulture` to both `SaveAs` and `SaveAsByTemplate`, verified by rendering a fractional value under `ru-RU` and asserting `1234.56`. Silent corruption: a nested object or array in the table path now throws `InvalidOperationException` naming the JSON path instead of writing a CLR type name into the cell. Ragged arrays: rows are aligned to the union of their keys instead of taking headers from the first item, which removes the `KeyNotFoundException`. Accepted: a template placeholder bound to a nested value still raises `IndexOutOfRangeException` from MiniExcel, because the engine cannot distinguish `{{key}}` from the supported `{{list.field}}` collection syntax without parsing the template's tokens. Both tests now state that this is a decision rather than an accident.
16. Stream artifacts end to end. **PARTIAL** — see item 20 for what landed and what remains. The inaccurate claim this item was raised against is corrected in §7.4.
17. ~~Stop shipping Development in the compose stack and gate developer-only surface.~~ **DONE** — compose now runs `ASPNETCORE_ENVIRONMENT=Production`, which also stops the developer exception page from returning stack traces and absolute source paths to any caller. OpenAPI and Scalar are opt-in through `DocumentProcessing:Api:ExposeOpenApi` and stay enabled in compose because they are documented; detailed health JSON is opt-in through `DocumentProcessing:Api:ExposeHealthDetails`, so anonymous probes answer with the bare status by default. Both remain automatic under Development. Three tests pin the gating.
18. ~~Add a CI gate so NuGet advisories cannot silently return.~~ **DONE** — `.github/workflows/ci.yml` builds Release with `-p:WarningsAsErrors=NU1902%3BNU1903`, then runs both suites and uploads results. The `%3B` escape is required: a literal `;` is split by the shell and MSBuild rejects the argument with MSB1006. The gate was verified in both directions by temporarily pinning MessagePack back to 2.5.192 and confirming the build failed with the advisory URLs, then reverting.
19. ~~Correct the remaining `localhost:7001` references.~~ **DONE** — all fifteen across README and `docs/performance-baseline.md` now use `http://localhost:5287`, which is the `dotnet run` address from `Properties/launchSettings.json`. The sample client's own default base address was changed separately and now points at `http://localhost:8081`, the Docker stack, because that is the configuration requiring a bearer token; every documented invocation passes an address explicitly, so the default only applies to a bare run. Do not "re-fix" the client default back to 5287 without changing the README examples too.
20. ~~Finish item 16's response-path copy.~~ **PARTIAL** — the sync and download endpoints pass their stream to `Results.File` rather than calling `ToByteArray()`, removing one full document copy per response, and the two places that copy an engine result into a `GeneratedDocumentResult` now dispose the source. Engines, the artifact store, and the cache still each hold a complete `byte[]`, so the deeper change is outstanding.
21. ~~Add a configurable engine that calls an external HTTP API.~~ **DONE** — `GenerateDocService.Engine.Api`, registered as `api`. Targets and credentials live in configuration (`DocumentProcessing:Api:Targets`), never in request data; a request may only *name* a pre-configured target through the `api.target` metadata key, so the engine cannot become an open proxy. Supports `None`, `Basic`, `Bearer` and `OAuth2ClientCredentials` with cached, skew-aware token refresh and a single in-flight fetch per token endpoint. Malformed targets fail at startup. Deliberately **no retry at any layer**: failures are wrapped in `NonRetryableGenerationException` so the processor's transient retry skips them too. Covered by `MockApiService`, a real Kestrel host on an ephemeral loopback port exercising every scheme, the unauthenticated path, the multipart contract, and a target-resolution test proving a named target cannot redirect to an arbitrary host. See §3.4 and architecture §5.5.
22. ~~Correct §7.4's claim that all engines stream.~~ **DONE** — the claim is now corrected in place in §7.4 rather than repeated, and architecture §9 item 5 states the actual memory behaviour. Making it *true* is the remaining half of item 20.
23. ~~Bring the documentation back in line with the code.~~ **DONE** — `docs/architecture.md` no longer claims authentication is absent or that there is no relational task store, its duplicate `Engine.QuestPdf` entry is removed, and its engine table is generated from the actual `[DocumentEngine]` attributes (nine engines, with `api`=10 through `fop`=300). `README.md` gained the missing Worker project, the four skills it never listed, and a corrected `csharp-developer` → `csharp-async` link. `README_ru.md` was truncated mid-word and has been completed and expanded. `docs/architecture_ru.md` was a one-line stub and is now a full Russian counterpart. `QWEN.md` was empty. `.github/copilot-instructions.md` no longer describes the repository as minimal.

**Next tasks**

24. Finish item 20: let engines write into a caller-supplied stream, and stream from the artifact store to the response and to MinIO instead of holding a full `byte[]` in the cache and store.
25. Add a compose-level end-to-end test that drives one request with all four external providers selected at once, which is the configuration the adapter tests do not cover.
26. Decide whether the console OpenTelemetry exporters should be conditional, now that compose runs as Production.
27. Prove the `api` engine against a real `fopconverter` stack end to end. Sample client example 16 currently only demonstrates that target resolution reaches the configured URL; it has never produced a PDF, because the converter stack was down when the example was added.
28. Capture a committed BenchmarkDotNet baseline so microbenchmark results have something to regress against.
29. Make sample client examples `14.1` and `15.1` independent of the working directory. `OfficeFixtures.TemplateRows(path)` calls `File.ReadAllBytes` with a relative path, which resolves against the process working directory. `dotnet run` keeps the invoking shell's directory, so the documented invocation from the repository root crashes with `System.IO.FileNotFoundException: Could not find file '<repo root>\template.docx'`. The `.csproj` already copies both templates to the output directory with `CopyToOutputDirectory=Always`, so resolving against `AppContext.BaseDirectory` would work from any directory and make that copy meaningful. The exception is also unhandled — it escapes while the request argument is being built, so it terminates the client instead of being reported as one failed example. Verified by running `14.1` from the repository root (crashes) and from the project directory (reaches the HTTP calls).

---

## 12. Final recommendation

Do not rewrite the solution.

The current architecture is good enough to evolve incrementally, and the persistence, security, validation and resilience work that once justified a "not production-ready" verdict is now done and tested.

The highest-value remaining path is resource handling and operational maturity: end-to-end streaming (item 24), a full-stack compose test (item 25), and a committed performance baseline (item 28) — while preserving the plugin and modular monolith structure. The `api` engine adds one new consideration to that list: the service now delegates to arbitrary configured third parties, so engine sandboxing (§8.2) is a more pressing question than it was when every engine ran in-process.
