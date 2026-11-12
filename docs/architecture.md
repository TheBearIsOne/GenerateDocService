# GenerateDocService Architecture

## 1. Project analysis summary

`GenerateDocService` is a modular monolith on .NET 10 for synchronous and asynchronous document generation.

The codebase already contains:

- Clean Architecture style separation between Domain, Application, Infrastructure, Presentation, Host, and Client;
- plugin-style document engines with metadata descriptors and runtime registry, currently nine engines;
- pluggable template parser providers with compiled template caching and engine-preferred parser resolution;
- sync and async processing flows;
- text and binary (base64) request transport, which is what makes the Office-file engines reachable over HTTP;
- request deduplication and generated artifact caching abstractions;
- durable task persistence through EF Core and PostgreSQL, with an outbox-backed dispatch and advisory-locked migrations;
- switchable infrastructure providers for in-memory vs Redis, in-memory vs RabbitMQ, in-memory vs MinIO/object storage, and in-memory vs PostgreSQL;
- JWT bearer authentication with role-based authorization policies;
- configurable request validation guardrails;
- bounded generation retries with a processing deadline, plus a standard HTTP resilience pipeline for the external FOP converter;
- artifact retention and cleanup, including reaping tasks abandoned in `Processing`;
- health checks, correlation propagation, logging, tracing, metrics, integration tests, NBomber performance baselines, and in-process BenchmarkDotNet microbenchmarks.

This is already beyond a scaffold: the repository is a working service foundation with several production-oriented decisions in place. Phase 1 of `docs/improvement-plan.md` (persistence, validation, auth, resilience) is complete; §9 lists what still blocks a full production-readiness claim.

---

## 2. Solution structure

## 2.1 Core modules

- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Domain`
  - core task state and domain concepts;
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application`
  - orchestration, registries, services, abstractions, messaging contracts, cache abstractions;
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure`
  - dependency injection, Redis/MinIO/RabbitMQ integrations, health checks, fake engine registration;
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation`
  - HTTP contracts and response mapping.

## 2.2 Host and clients

- `src/Host/GenerateDocService.Api`
  - minimal API host, middleware, JWT authentication and authorization policies, OpenTelemetry, Serilog, health endpoints;
- `src/Host/GenerateDocService.Worker`
  - dedicated background processing host; runs the MassTransit consumer independently of HTTP ingress and shares the same Application and Infrastructure layers, so API and Worker scale separately;
- `src/Clients/GenerateDocService.SampleClient`
  - demo client with an interactive menu over every reachable engine, and hosted BenchmarkDotNet microbenchmarks under `Benchmarks/`.

## 2.3 Engines

- `src/Engines/GenerateDocService.Engine.Abstractions`
  - engine and parser contracts, metadata attributes, descriptors, and `NonRetryableGenerationException`;
- `src/Engines/GenerateDocService.Engine.Scriban`
  - Scriban document engine and template parser;
- `src/Engines/GenerateDocService.Engine.DotLiquid`
  - DotLiquid document engine and template parser;
- `src/Engines/GenerateDocService.Engine.QuestPdf`
  - JSON-to-PDF generation with QuestPDF.
- `src/Engines/GenerateDocService.Engine.MiniExcel`
  - XLSX generation and template population with MiniExcel.
- `src/Engines/GenerateDocService.Engine.MiniWord`
  - DOCX template population with MiniWord.
- `src/Engines/GenerateDocService.Engine.MiniPdf`
  - DOCX/XLSX-to-PDF conversion.
- `src/Engines/GenerateDocService.Engine.Fop`
  - HTTP adapter for FO/XSL-FO-to-PDF conversion through the external FOP Converter API.
- `src/Engines/GenerateDocService.Engine.Api`
  - configurable generalisation of `fop`: posts the payload to a named external HTTP target and returns the response body as the document. Targets and their credentials live in configuration, never in request data.

A ninth engine, `fake`, is registered from `GenerateDocService.DocumentProcessing.Infrastructure/Engines/` rather than from its own project; it exists for smoke tests and as a routing fallback.

## 2.4 Tests

- `tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests`
  - retains inspectable output from every in-process engine in `artifacts/test-results/<engine>/` for local inspection;
  - constructs TXT, HTML, Markdown, XLSX, and DOCX templates with JSON fixture data; XLSX and DOCX tests inspect Open XML parts to confirm populated values and remove placeholder tokens;
  - normalizes prefixed SpreadsheetML produced by MiniExcel before MiniPdf conversion so worksheet values are rendered;
  - covers retry classification and backoff, in-memory TTL and capacity bounds, retention cleanup, and resilience DI registration;
- `tests/Integration/GenerateDocService.Api.IntegrationTests`
  - runs an opt-in Scriban XSL-FO/XML template-to-FOP converter test when `FOP_CONVERTER_BASE_URL` is configured and writes its PDF to `artifacts/test-results/fop/`;
  - hosts `MockApiService`, a real Kestrel service used to exercise the `api` engine across every authentication scheme plus the unauthenticated path;
  - carries `Category=RequiresDocker` tests that start real PostgreSQL, RabbitMQ, Redis and MinIO containers through Testcontainers, and skip cleanly when no Docker daemon is reachable;
- `tests/Performance/GenerateDocService.LoadTests.NBomber`
  - end-to-end HTTP load profiles; the component-level counterpart lives in the sample client.

---

## 3. Architectural style

The service follows a hybrid of:

- Clean Architecture;
- Modular Monolith;
- Plugin Architecture for engines and template parsers;
- Message-driven async processing.

### 3.1 Layer responsibilities

#### Domain
Contains task lifecycle concepts and state transitions.

#### Application
Contains orchestration and all business workflow logic:

- engine selection;
- sync generation;
- async enqueueing;
- background processing;
- deduplication;
- artifact caching;
- task state transitions;
- metrics recording.

#### Infrastructure
Contains technical adapters and provider switching:

- DI registration, including provider selection through `services.Replace(...)`;
- Redis cache and dedup implementations;
- bounded in-memory cache and dedup implementations with TTL and capacity limits;
- MinIO artifact storage;
- MassTransit bus integration and the PostgreSQL outbox dispatcher;
- EF Core PostgreSQL persistence, migrations, and advisory-locked schema application at startup;
- `ArtifactRetentionCleaner` (the cleanup policy) and `ArtifactCleanupBackgroundService` (only its scheduler);
- resilience registration: the generation retry options, the `TransientFailureClassifier`, and the FOP `HttpClient`'s standard resilience handler;
- `api` engine target registration and validation, which fails at startup rather than on the first request;
- health checks;
- assembly scanning for engines and template parsers.

#### Presentation
Contains API contracts and response mapping. Request validation and authorization policies deliberately do **not** live here — they are host concerns, in `src/Host/GenerateDocService.Api/Validation` and `.../Security`.

#### Host
Contains transport wiring:

- HTTP endpoints;
- middleware;
- request validation guardrails (`GenerateDocumentRequestValidator`, applied as an endpoint filter);
- JWT authentication and role-based authorization policies, conditionally applied via `MaybeRequireAuth`;
- logging;
- tracing;
- metrics export;
- OpenAPI and the Scalar UI, gated by `DocumentProcessing:Api:ExposeOpenApi`.

### 3.2 Fault-tolerance model

The design already embeds several resilience patterns that keep generation jobs robust under transient failure:

- background generation retries transient faults with exponential backoff until the configured deadline is reached. `TransientFailureClassifier` decides what counts as transient — by default `HttpRequestException`, `TimeoutException`, `IOException`, and `SocketException`, plus `RedisConnectionException` when the Redis provider is enabled. It walks the whole inner-exception chain;
- an engine can opt a failure **out** of that retry by throwing `NonRetryableGenerationException`. The classifier checks for it before any transient type, at every level of the chain, so the opt-out always wins. The `api` engine uses this because a configured target is an unknown third party whose endpoints may not be idempotent — the request may already have been received before the failure surfaced, so an automatic retry could duplicate a side effect. `fop` is the opposite case: its converter is idempotent, so it keeps both its HTTP resilience pipeline and the processor's retries;
- cancellation is always terminal. An expired processing deadline is never retried into another full attempt, and `DocumentGenerationFailed` is published once after the final attempt rather than per retry;
- the PostgreSQL outbox and task lease pattern ensures async work is not lost when the API or worker is restarted, and duplicate delivery is tolerated by the task state machine;
- the FOP converter uses a standard `HttpClient` resilience handler with per-attempt timeout, total timeout, retry windows, and a circuit breaker so converter outages do not cascade into unbounded wait states. `HttpStandardResilienceOptions` requires the sampling window to cover at least two attempt timeouts and the total budget to cover every attempt; registration widens both automatically so a hand-edited configuration cannot turn the first request into an `OptionsValidationException`;
- `ArtifactRetentionCleaner` fails tasks left in `Processing` beyond `StuckProcessingTaskSeconds`, so a worker that crashed mid-generation cannot leave a client polling forever or a fingerprint pinned to a task that will never finish;
- generated document and request deduplication caches reduce repeated work and help the system recover from transient duplicate submissions without running the same job repeatedly;
- in-memory providers enforce TTL and bounded capacity so local development remains safe under load and does not leak memory indefinitely;
- health checks cover engine readiness plus every selected infrastructure provider, so operational issues are visible before they affect user requests.

This is not a full distributed-failure model yet, but it represents a solid fault-tolerant foundation for a modular monolith and gives the project a practical path toward a more production-grade deployment. §9 lists the remaining gaps, chiefly the absence of per-client policies around Redis, RabbitMQ and MinIO themselves and of any bulkhead or rate limiting on generation concurrency.

---

## 4. Main building blocks

## 4.1 Engine subsystem

The engine subsystem is one of the strongest parts of the design.

Key elements:

- `IDocumentGenerationEngine`
- `DocumentEngineAttribute`
- `DocumentGenerationEngineDescriptor`
- `DocumentGenerationEngineRegistry`

Behavior:

- engines are discovered through assembly scanning;
- only attributed engines are registered;
- descriptors expose supported input/output/template formats and priority;
- the registry can resolve by name or find candidates by capability;
- if request `engine` is missing, the highest-priority matching engine is chosen, ties broken by name ascending.

### Current engine capabilities and priorities

Read from the `[DocumentEngine]` attributes, so this is the routing table the registry actually uses:

| Engine | Input | Output | Template | Priority |
|--------|-------|--------|----------|----------|
| `api` | any | any | — | 10 |
| `fake` | json, xml, csv, html, markdown | txt, json, html | scriban, liquid, html, markdown | 0 |
| `dotliquid` | json | txt, html, json, md, markdown | liquid, dotliquid, txt, html, md, markdown | 90 |
| `scriban` | json | txt, html, json, md, markdown | scriban, txt, html, md, markdown | 100 |
| `miniword` | json | docx | miniword, docx | 100 |
| `miniexcel` | json | xlsx | xlsx | 150 |
| `questpdf` | json | pdf | json, txt, html, md, markdown | 200 |
| `minipdf` | docx, xlsx | pdf | — | 250 |
| `fop` | fo, xsl-fo | pdf | — | 300 |

Two consequences of this table are worth stating explicitly:

- **`api` declares no formats.** Its capabilities come from configuration rather than metadata, so it registers with empty arrays and `/api/v1/engines` lists it with no formats. Auto-selection still resolves it correctly because `CanHandle` consults the configured targets. Its priority of 10 is the lowest of the real engines, so a purpose-built engine always wins when both could serve a request — `fop` at 300 takes `xsl-fo -> pdf` even though the preconfigured `converter` target also matches it.
- **`fake` at priority 0 is a deliberate floor.** It matches broadly but loses to every real engine, so it can only be selected explicitly or when nothing else qualifies.

This makes engine routing deterministic and extensible.

## 4.2 Template parser subsystem

Key elements:

- `ITemplateParser`
- `ITemplateParserRegistry`
- `ICompiledTemplateCache`
- `TemplateParserRegistry`
- `ScribanTemplateParser`
- `DotLiquidTemplateParser`

Behavior:

- engines delegate template parsing to parser providers;
- compiled templates are cached by hash;
- cache provider can be in-memory or Redis-backed;
- parser registry allows parser resolution by template format.

`ITemplateParserRegistry.Resolve` takes an optional `preferredParserName`, and each engine passes its own name. This matters because template formats overlap: both `ScribanTemplateParser` and `DotLiquidTemplateParser` claim `txt`, `html`, `md`, and `markdown`. Without a preference a contested format resolved alphabetically, so `engine=scriban` with `templateFormat=html` could be handed a DotLiquid compilation the Scriban engine cannot execute — an HTTP 500. With the preference the parser that can actually use the result wins. When no preference is supplied, or the preferred parser is not among the candidates, the old alphabetical fallback is retained and pinned by a test so changing it stays deliberate.

One asymmetry is worth knowing: `RedisCompiledTemplateCache` silently stores nothing for DotLiquid compilations, so under the Redis provider those templates re-parse on every request. A test pins this behaviour rather than hiding it.

This is a good separation because template parsing is a distinct hotspot from generation itself.

## 4.3 Async processing subsystem

Key elements:

- `AsyncDocumentGenerationService`
- `BackgroundDocumentGenerationProcessor`
- `IBackgroundGenerationScheduler`
- `GenerateDocumentRequested`
- `DocumentGenerated`
- `DocumentGenerationFailed`
- `IDocumentGenerationTaskRepository`
- `GenerationResilienceOptions` and `TransientFailureClassifier`
- `ArtifactRetentionCleaner`

Behavior:

- API accepts async request;
- request is normalized and enriched with correlation metadata;
- dedup fingerprint is created;
- task and serialized generation message are committed together when PostgreSQL is selected;
- a PostgreSQL outbox dispatcher claims pending messages with a lease and sends them through MassTransit;
- dispatch failures retry with backoff and eventually move the task to `Failed`;
- background processor performs generation and artifact save, retrying classified transient faults under a processing deadline;
- task status is updated and lifecycle events are published best-effort, once, after the final attempt;
- separately, `ArtifactRetentionCleaner` removes completed and failed tasks past their retention windows and fails tasks stuck in `Processing`.

The retry decision is made by walking the exception chain once, in this order: a `NonRetryableGenerationException` anywhere in the chain ends retrying, then a cancellation ends it, then the first matching transient type enables it. That ordering is what lets an engine veto retry for a call whose side effects it cannot guarantee are safe to repeat.

## 4.4 Caching and storage subsystem

Key elements:

- `IGenerationRequestDeduplicationStore`
- `IGeneratedDocumentCache`
- `IDocumentArtifactStore`
- Redis cache implementations;
- MinIO artifact store implementation.

Behavior:

- dedup prevents duplicate async work for the same request fingerprint;
- generated document cache can short-circuit repeated generation;
- artifact storage is abstracted away from the task model.

## 4.5 Infrastructure technology usage

No engine project directly references or calls Redis, MinIO, or MassTransit. Engines receive requests through `IDocumentGenerationEngine` and return results; shared application services use cache, storage, and messaging abstractions. Scriban and DotLiquid template parsers do use `ICompiledTemplateCache`, which can be backed by Redis when that provider is selected. MinIO artifact storage and MassTransit messaging remain outside the engine calls. The Infrastructure project registers the selected implementations for both API and Worker hosts.

Two engines do make **outbound HTTP** calls, through `IHttpClientFactory` rather than a constructed client: `fop` calls the external converter, and `api` calls whatever named target configuration resolves to. Both clients are registered by Infrastructure, which is where their resilience behaviour is decided — the FOP client gets a standard resilience handler, and the `api` clients deliberately get none, because retrying an unknown third party is exactly what `NonRetryableGenerationException` exists to prevent.

| Technology | Where it is used | How it is used |
|------------|-----------------|----------------|
| Redis (`StackExchange.Redis`) | Infrastructure caching adapters | Replaces `ICompiledTemplateCache`, `IGeneratedDocumentCache`, and `IGenerationRequestDeduplicationStore`. Compiled templates and completed document references are best-effort caches; a transient cache outage is treated as a miss. Request-deduplication reservations remain fail-closed to avoid cross-instance duplicate work. |
| MinIO (`Minio` .NET client) | `MinioDocumentArtifactStore` | Implements `IDocumentArtifactStore`. The worker writes generated files to the configured bucket under `{requestId}/{fileName}`; task records hold the resulting storage path, and the download endpoint reads the artifact through the same abstraction. |
| MassTransit | Infrastructure messaging adapters | The PostgreSQL outbox dispatcher sends `GenerateDocumentRequested` to the configured RabbitMQ queue. A conditional database lease coordinates dispatchers in API/Worker hosts; the pending payload is cleared after broker acceptance. In-memory persistence submits directly. Generated/failed lifecycle events are published through `MassTransitDocumentGenerationEventPublisher`. |
| PostgreSQL (`Npgsql` + EF Core) | `PostgreSqlDocumentGenerationTaskRepository`, outbox dispatcher, migrations | Durable task state and the pending command payload, committed atomically. Both hosts apply pending migrations at startup behind a PostgreSQL advisory lock so concurrent starts cannot race on DDL. `GetAsync` is `AsNoTracking`, so a caller that adds and then updates the same entity must use a fresh scope per phase. |
| Outbound HTTP (`IHttpClientFactory`) | `fop` and `api` engines | `fop` posts multipart `primaryDocument` to the external converter through a client carrying `AddStandardResilienceHandler`. `api` uses two clients — one per request, one for OAuth2 token exchange — and neither carries a resilience handler. |

MassTransit is deliberately pinned to **8.5.10**. Version 9 requires a paid licence, so upgrading is a commercial decision rather than a routine dependency bump; do not raise it as part of unrelated work.

These adapters are not mandatory in every run. `appsettings.json` and Development settings select in-memory caching, messaging, storage, and persistence by default. `deploy/docker/docker-compose.yml` configures Redis, RabbitMQ through MassTransit, MinIO, and PostgreSQL for both API and Worker. PostgreSQL stores task state and the pending command payload atomically; Redis does not replace the task repository. The outbox is at-least-once, so the consumer claims a task atomically and ignores duplicate deliveries after a task has left `Queued`.

---

## 5. Runtime call flows

## 5.1 Sync request flow

```text
Client
  -> POST /api/v1/documents/sync
  -> CorrelationIdMiddleware injects correlation metadata
  -> JWT bearer authentication + role policy (when auth is enabled)
  -> ValidationRequestFilter endpoint filter, delegating to GenerateDocumentRequestValidator
       -> size limits measured against the DECODED base64 length
       -> payload/payloadBase64 and template/templateBase64 mutual exclusion
       -> 400 with field-level details on any violation
  -> Program.cs maps HTTP contract to GenerateDocumentRequest, decoding base64 once
  -> SyncDocumentGenerationService
       -> ResolveRequest()
            -> explicit engine OR auto-select by capability/priority
       -> registry.Resolve(engine)
       -> engine.GenerateAsync(request)
            -> template parser registry resolves parser, preferring the engine's own name
            -> compiled template cache may be used
       -> DocumentGenerationMetrics record success/failure
  -> API returns the result stream through Results.File, without copying to a byte[]
```

The sync path has **no retry**: a failure is returned to the caller, who decides whether to resubmit. Bounded retry exists only in the background processor, where nobody is waiting on the connection.

## 5.2 Async request flow

```text
Client
  -> POST /api/v1/documents/async
  -> Program.cs maps HTTP contract to GenerateDocumentRequest
  -> CorrelationIdMiddleware injects correlation metadata
  -> AsyncDocumentGenerationService
       -> ResolveRequest()
       -> create request fingerprint
       -> reserve dedup entry
      -> persist task and outbox payload together
  -> API returns 202 Accepted + task status payload

    Outbox dispatcher
      -> lease pending outbox row
      -> send GenerateDocumentRequested through MassTransit
      -> clear pending payload after broker acceptance
      -> retry with backoff; mark task Failed after the configured attempt limit

Background consumer/processor
  -> receives GenerateDocumentRequested
  -> BackgroundDocumentGenerationProcessor
       -> load task
       -> mark Processing
       -> check generated document cache by fingerprint
       -> if miss: resolve engine and generate document
       -> save artifact to store
       -> update generated document cache
       -> mark Completed or Failed
       -> publish DocumentGenerated or DocumentGenerationFailed

Client
  -> GET /api/v1/tasks/{taskId}
  -> GET /api/v1/tasks/{taskId}/download
```

The sequence diagram for the Compose configuration (RabbitMQ, Redis, MinIO, and PostgreSQL) is in [async-generation-sequence.mmd](async-generation-sequence.mmd). The [MiniExcel async sequence](miniexcel-async-sequence-sc.mmd) details the `json -> xlsx` path, including outbox dispatch, template and cache branches. With the default in-memory providers, the same application abstractions are used without those external services or the PostgreSQL outbox.

## 5.3 Auto-selection flow

```text
Request without engine
  -> registry.FindCandidateDescriptors(input, output, templateFormat)
  -> order by Priority desc, then Name asc
  -> take first candidate
  -> continue with resolved engine name
```

## 5.4 Template rendering flow

```text
Engine
  -> choose parser from ITemplateParserRegistry, passing its own name as the preference
  -> hash template content
  -> try ICompiledTemplateCache
  -> if miss: compile template and cache it
  -> render against JSON payload model
  -> return GeneratedDocumentResult
```

## 5.5 `api` engine target resolution

The `api` engine has no formats of its own, so routing to a concrete endpoint is a second resolution step that happens *inside* the engine, after the registry has already chosen it. `ResolveTarget` runs four rules in order and throws rather than silently degrading:

```text
1. metadata["api.target"] present and non-empty
     -> target must EXIST          (else InvalidOperationException)
     -> target must MATCH formats   (else InvalidOperationException)
     -> no fall-through to the next rule on failure
2. else DefaultTarget configured
     -> must exist                 (else InvalidOperationException)
     -> returned WITHOUT a format check
3. else first target, ordered by name (OrdinalIgnoreCase), whose declared formats match
4. else InvalidOperationException naming both the metadata key and the DefaultTarget setting
```

Three properties of this order are deliberate:

- **A named target that does not match is a hard error, not a fallback.** If a caller asks for `converter` and `converter` cannot serve `json -> pdf`, the engine refuses instead of quietly picking a different endpoint. Silent substitution would make the response's origin unauditable.
- **`DefaultTarget` skips the format check.** It is an operator's explicit "send everything else here" instruction, so it is honoured as given. Rule 1 and rule 3 are both format-checked; rule 2 is not. This is the one place where a mismatch can reach the network, and it is why `DefaultTarget` should name a general-purpose endpoint.
- **Rule 3 sorts by name**, not by dictionary enumeration order, so resolution is deterministic across runs and across configuration reloads.

`Matches` treats a target's `InputFormat`/`OutputFormat` as wildcards when they are blank, which is what lets one target serve several format pairs.

Resolution is also the engine's **security boundary**: every branch reads from `options.Value.Targets`, so the request can select a target but can never supply a URL. A test pins this by configuring a target pointing at a link-local metadata address and asserting the request fails with zero calls made to the mock upstream — proving that naming a target cannot redirect traffic to an arbitrary host.

Failures are wrapped in `NonRetryableGenerationException`, except cancellation, which propagates so an expired deadline is not mistaken for an upstream fault.

---

## 6. Infrastructure configuration model

The application can run in lightweight mode or more production-like mode. Four sections select a provider, and each has an in-memory default so `dotnet run` works with no infrastructure present.

### 6.1 Provider selection

| Section | Values | Replaces |
|---------|--------|----------|
| `DocumentProcessing:Persistence:Provider` | `InMemory`, `PostgreSql` | `IDocumentGenerationTaskRepository`, and enables the outbox dispatcher |
| `DocumentProcessing:Caching:Provider` | `InMemory`, `Redis` | `ICompiledTemplateCache`, `IGeneratedDocumentCache`, `IGenerationRequestDeduplicationStore` |
| `DocumentProcessing:Messaging:Transport` | `InMemory`, `RabbitMQ` | the MassTransit bus and the `GenerateDocumentRequested` consumer |
| `DocumentProcessing:Storage:Provider` | `InMemory`, `ObjectStorage` | `IDocumentArtifactStore` (MinIO) |

Selection happens through `services.Replace(...)`, so the application layer never learns which implementation it was given. With `InMemory` messaging the scheduler *publishes* and MassTransit routes by convention; with `RabbitMQ` it *sends* to the queue named by `DocumentProcessing:Messaging:QueueName` (default `document-generation`), which must match the consumer's receive endpoint or messages sit unconsumed.

### 6.2 Behavioural settings

| Section | Purpose |
|---------|---------|
| `DocumentProcessing:Resilience` | `ProcessingTimeoutSeconds` (300), `MaxAttempts` (3), `InitialDelayMilliseconds` (250), `MaxDelayMilliseconds` (5000), `BackoffMultiplier` (2.0) |
| `DocumentProcessing:InMemory` | `GeneratedDocumentTtlMinutes` (60), `MaxCachedDocuments` (1000), `DeduplicationTtlMinutes` (15); `0` disables expiry |
| `DocumentProcessing:Retention` | `RetentionDays` (30), `FailedTaskRetentionDays` (7), `StuckProcessingTaskSeconds` (1800), `CleanupIntervalHours` (1) |
| `DocumentProcessing:Validation` | `MaxPayloadSizeBytes` (10 MB), `MaxTemplateSizeBytes` (5 MB), `MaxMetadataEntries` (50), `MaxMetadataKeyLength` (128), `MaxMetadataValueLength` (1024), `MaxEngineNameLength` (64), `MaxFormatNameLength` (64), `MaxRequestIdLength` (64) |
| `DocumentProcessing:Fop` | the converter's `BaseUrl` plus the seven resilience-pipeline settings |
| `DocumentProcessing:Authentication` | `Enabled`, `Issuer`, `Audience`, `SigningKey` |
| `DocumentProcessing:Api` | shared by two option classes — see below |

`DocumentProcessing:Api` is read by **two** unrelated classes because both concern the HTTP surface: `ApiHostOptions` in the API host (`ExposeOpenApi`, `ExposeHealthDetails`) and `DocumentProcessingApiOptions` in the `api` engine (`Targets`, `DefaultTarget`). They bind different keys from the same section, so adding a key to either is safe, but the shared name is worth knowing before grepping.

### 6.3 `api` engine targets

Targets are configuration, never request data. A request may only *name* a pre-configured target through the `api.target` metadata key, so the engine cannot be turned into an open proxy and no secret travels in a request body. `DocumentProcessingApiOptions.Validate()` runs at registration, so a malformed target — a relative `BaseUrl`, an empty `RequestPath`, a `Basic` target with no credentials, a `DefaultTarget` naming a target that does not exist — fails the host at **startup** rather than on the first request.

Each target declares `BaseUrl`, `RequestPath` (with `{requestId}`/`{correlationId}` substitution), `Method`, `ContentType`, optional `MultipartFieldName`, optional `InputFormat`/`OutputFormat`, `TimeoutSeconds`, and an `Authentication` block whose `Scheme` is `None`, `Basic`, `Bearer`, or `OAuth2ClientCredentials`.

### 6.4 Local docker stack

`deploy/docker/docker-compose.yml` starts:

- API and Worker
- RabbitMQ
- Redis
- MinIO
- PostgreSQL

The stack configures both API and Worker to use these external providers, and runs `ASPNETCORE_ENVIRONMENT=Production`. Infrastructure ports are bound to `127.0.0.1`, and the API is published on host port **8081** (container 8080) with Redis on **6380** (container 6379) so the stack can run alongside the separate `fopconverter` project, which claims 8080 and 6379. Containers still address each other by service name over the internal network, so the remapping is host-side only.

Authentication is enabled in the stack, so `GDS_AUTH_SIGNING_KEY` must be supplied through `deploy/docker/.env`; compose fails fast if it is missing. The FOP converter is external to this compose file and is configured separately through `FOP_CONVERTER_BASE_URL`, defaulting to `http://host.docker.internal:8080/` because `localhost:8080` inside a container is the container itself.

---

## 7. Observability model

The host already includes a useful observability foundation.

### 7.1 Logging

- Serilog console logging;
- log context enrichment;
- request logging;
- correlation propagation.

### 7.2 Tracing

- ASP.NET Core instrumentation;
- outgoing HTTP instrumentation;
- MassTransit activity source;
- optional OTLP export.

### 7.3 Metrics

Emitted from the meter `GenerateDocService.DocumentProcessing`:

- `document_generation_sync_requests_total`
- `document_generation_async_requests_total`
- `document_generation_completed_total`
- `document_generation_failed_total`
- `document_generation_duration_ms`

Every instrument carries the tags `engine` and `output_format`. Duration is recorded on both the completed and failed paths, so a failure still contributes a latency sample rather than disappearing from the histogram.

Note what these counters do **not** show: `document_generation_failed_total` is recorded once per task, after the final attempt, not once per attempt. A job that burned three retries therefore increments the failure counter once, exactly like one that failed immediately. Retry pressure is visible in `document_generation_duration_ms`, not in the failure count — which is how the `api` engine's missing retry opt-out was first spotted, as a 13-second duration on an upstream that was simply refusing connections.

### 7.4 Health checks

Registrations are tagged, and `/health/live` and `/health/ready` filter on those tags:

| Check | Tags | When registered |
|-------|------|-----------------|
| `document-engines` | live, ready | always |
| `authentication` | live, ready | always |
| `redis-cache` | ready | Redis caching provider |
| `cache-provider` | live, ready | in-memory caching provider |
| `postgresql-persistence` | ready | PostgreSQL persistence provider |
| `task-persistence` | live, ready | in-memory persistence provider |
| `rabbitmq-transport` | ready | RabbitMQ transport |
| `messaging-transport` | live, ready | in-memory transport |
| `object-storage` | ready | MinIO/object storage provider |
| `artifact-storage` | live, ready | in-memory storage provider |

The split is deliberate: external dependencies are `ready`-only, so a Redis outage drains traffic without the orchestrator killing an otherwise healthy container. The in-memory fallbacks carry both tags because there is nothing external to wait for.

Both probes are **anonymous**, so container and orchestrator probes keep working when authentication is enabled. That also means the response writer is gated: detailed per-check JSON exposes engine implementation class names such as `ScribanDocumentGenerationEngine`, so outside Development the probes answer with the bare overall status unless `DocumentProcessing:Api:ExposeHealthDetails` is set. `HealthCheckResponseWriters.WriteMinimalUIResponse` is `internal` to ASP.NET Core, which is why the host supplies its own minimal writer.

---

## 8. Strengths of the current solution

## 8.1 Strong points

1. Good separation of concerns.
2. Real plugin architecture for engines.
3. Clear extensibility model for formats and parsers.
4. Async flow is already close to production shape.
5. Observability is not postponed.
6. Performance baseline project already exists.
7. Auto-selection logic reduces client coupling.
8. Infrastructure switching keeps local development simple.
9. Security-sensitive decisions default closed: the signing key has no committed default, the developer surface is opt-in, and `api` targets cannot be redirected per request.

## 8.2 What is already production-oriented

- correlation propagation;
- deduplication;
- object storage abstraction;
- durable task persistence with outbox-backed dispatch;
- JWT authentication with role-based authorization;
- request validation guardrails;
- bus-based async processing;
- health readiness split;
- metrics and tracing;
- fault-tolerant generation lifecycle with retry deadlines, terminal cancellation, per-engine retry opt-out, and circuit breaking for external HTTP dependencies;
- artifact retention and abandoned-task reaping;
- a CI gate that fails the build on NuGet vulnerability advisories;
- integration coverage for core API behavior, including container-backed provider tests.

---

## 9. Current limitations

Two limitations recorded in earlier revisions of this document are **no longer true** and are kept here only so the correction is visible: API security/authentication *is* present (JWT bearer with role policies), and a persistent relational task store *does* exist (EF Core over PostgreSQL, with advisory-locked migrations). What follows is the current list.

Structural:

1. There is only one bounded context so far.
2. No formal versioning strategy for engine contracts.
3. No tenant isolation model.
4. No explicit admin/operations endpoints beyond health and task status.

Resource handling:

5. Generation is **not streamed end to end**. Both HTTP response paths hand their stream to `Results.File` rather than copying to a `byte[]`, but every engine still materialises its whole document in memory, and the artifact store and the generated-document cache each hold a full copy. Base64 request transport adds roughly 33% on top for binary input. Any claim that "all engines use streaming approaches" is inaccurate.
6. Size governance is a request-level guardrail (10 MB payload, 5 MB template) rather than a quota or admission-control model, so aggregate memory pressure under concurrency is unbounded.

Resilience:

7. Redis, RabbitMQ, and MinIO clients have no explicit per-client retry or circuit-breaker policy; only failures surfacing through the generation workflow are classified and retried.
8. There is no bulkhead or rate limiting on generation concurrency.

Test and operational coverage:

9. Redis and MinIO are covered at the adapter level only. No test drives one request through the compose stack with all four external providers selected at once, which is the configuration that actually ships.
10. Compose still exports OpenTelemetry traces and metrics to the console, which is developer-oriented output for a stack that now runs as Production.
11. Benchmark results are not captured as a committed baseline, so there is nothing to regress against.

Accepted defect:

12. On the MiniExcel *template* path, a `{{placeholder}}` bound to a nested object or array raises an unhandled `IndexOutOfRangeException` from MiniExcel's renderer. This is a decision, not an oversight: rejecting nesting there would break the supported `{{items.field}}` repeating-row syntax, and telling the two apart requires parsing the workbook's template tokens. The other three characterized MiniExcel defects (culture-dependent number formatting, silent cell corruption from nested values, ragged-array header loss) are fixed.

`docs/improvement-plan.md` §11 tracks these as numbered tasks with their current status.

---

## 10. Recommended target architecture direction

Short term, keep the modular monolith.

Reason:

- current size does not justify distributed decomposition;
- boundaries are becoming clear but are still evolving;
- keeping a single deployable preserves development speed.

Medium term, evolve toward:

- ~~dedicated durable task persistence~~ — **done** (EF Core + PostgreSQL, outbox dispatch);
- ~~separate worker host for background generation~~ — **done** (`GenerateDocService.Worker`);
- ~~stronger artifact lifecycle management~~ — **done** (`ArtifactRetentionCleaner`, including abandoned-task reaping);
- stronger configuration and secret handling — **partially done**: the signing key has no committed default and `api` target secrets are validated at startup, but there is still no secret store or rotation story, and compose exports telemetry to the console;
- end-to-end streaming, so a large document does not need three full in-memory copies (§9 item 5);
- per-client resilience for Redis, RabbitMQ and MinIO, plus a bulkhead or rate limit on generation concurrency (§9 items 7 and 8);
- engine sandboxing/isolation for high-risk converters — now more pressing, since the `api` engine delegates to arbitrary configured third parties.

Long term, split only the components that justify independent scaling:

- API gateway/ingress;
- background workers;
- heavyweight conversion engines;
- reporting/analytics pipeline.

---

## 11. Architecture diagrams

## 11.1 High-level component diagram

```text
+-------------------+        +-----------------------------------+
|   Sample Client   | -----> |      GenerateDocService.Api       |
| - menu, 16 ex.    |        |  - Minimal API endpoints          |
| - BenchmarkDotNet |        |  - Correlation middleware         |
+-------------------+        |  - JWT auth + role policies       |
                             |  - Validation endpoint filter     |
                             |  - Health / Telemetry / OpenAPI   |
                             +-----------------+-----------------+
                                               |
                                               v
                             +-----------------------------------+
                             |   DocumentProcessing.Application   |
                             | - Sync / Async services            |
                             | - Background processor + retry     |
                             | - TransientFailureClassifier       |
                             | - Registries / Metrics             |
                             +--+--------------+---------------+--+
                                |              |               |
             +------------------+              |               +------------------+
             v                                 v                                  v
+--------------------------------+  +----------------------------+  +--------------------------------+
| Engine Registry / Parsers      |  | Messaging / Tasks          |  | Cache / Artifact Storage       |
| fake, scriban, dotliquid,      |  | - MassTransit 8.5.10       |  | - InMemory / Redis             |
| questpdf, miniexcel, miniword, |  | - InMemory / RabbitMQ      |  | - InMemory / MinIO             |
| minipdf, fop, api              |  | - InMemory / PostgreSQL    |  | - Dedup / generated cache      |
| - Scriban + DotLiquid parsers  |  | - Outbox dispatcher        |  | - Retention cleaner            |
+---------------+----------------+  | - Artifact retention       |  +--------------------------------+
                |                   +----------------------------+
                v
+--------------------------------+
| External HTTP dependencies     |
| - FOP converter (fop engine)   |
| - configured targets (api)     |
+--------------------------------+

                    +-----------------------------------+
                    |     GenerateDocService.Worker      |
                    |  - MassTransit consumer            |
                    |  - shares Application + Infra      |
                    |  - no published HTTP surface       |
                    +-----------------------------------+
```

## 11.2 Sync sequence diagram

```text
Client -> Correlation -> Auth -> Validation -> SyncDocumentGenerationService -> EngineRegistry -> Engine -> ParserRegistry/TemplateCache -> Metrics -> Results.File -> Client
```

No retry: the caller receives the failure and decides whether to resubmit.

## 11.3 Async sequence diagram

```text
Client -> Correlation -> Auth -> Validation -> AsyncDocumentGenerationService -> DedupStore -> TaskRepository(+outbox) -> 202 Accepted
OutboxDispatcher -> lease -> MassTransit Send(queue) -> clear payload   [retry w/ backoff; Failed after the attempt limit]
Bus -> Worker -> BackgroundDocumentGenerationProcessor
                   -> load task, mark Processing (once)
                   -> generated-document cache lookup by fingerprint
                   -> on miss: resolve engine, generate, save artifact, update cache
                   -> attempt loop bounded by MaxAttempts under ProcessingTimeoutSeconds
                        TransientFailureClassifier: NonRetryable > Cancellation > transient type
                   -> mark Completed or Failed, record metrics once
                   -> publish DocumentGenerated or DocumentGenerationFailed once
Client -> TaskStatus API -> Download API
ArtifactRetentionCleaner (scheduled) -> delete expired artifacts/records; fail tasks stuck in Processing
```

---

## 12. Conclusion

The project is in a strong intermediate state:

- more mature than a scaffold;
- still simple enough to evolve safely;
- architecturally sound for iterative hardening.

Persistence, authentication, validation, and generation resilience — Phase 1 of the improvement plan — are complete, so the remaining work is no longer about making the service correct but about making it economical under load and observable in operation: streaming, per-client resilience, concurrency limits, a full-stack compose test, and a committed performance baseline.

The best next steps are not a rewrite, but targeted improvements in resource handling, operational maturity, and governance.
