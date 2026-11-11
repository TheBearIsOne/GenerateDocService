# GenerateDocService Architecture

## 1. Project analysis summary

`GenerateDocService` is a modular monolith on .NET 10 for synchronous and asynchronous document generation.

The codebase already contains:

- Clean Architecture style separation between Domain, Application, Infrastructure, Presentation, Host, and Client;
- plugin-style document engines with metadata descriptors and runtime registry;
- pluggable template parser providers with compiled template caching;
- sync and async processing flows;
- request deduplication and generated artifact caching abstractions;
- switchable infrastructure providers for in-memory vs Redis, in-memory vs RabbitMQ, and in-memory vs MinIO/object storage;
- health checks, correlation propagation, logging, tracing, metrics, integration tests, and NBomber performance baselines.

This is already beyond a scaffold: the repository is a working service foundation with several production-oriented decisions in place.

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
  - minimal API host, middleware, OpenTelemetry, Serilog, health endpoints;
- `src/Clients/GenerateDocService.SampleClient`
  - demo client for engine discovery and sync/async calls.

## 2.3 Engines

- `src/Engines/GenerateDocService.Engine.Abstractions`
  - engine and parser contracts, metadata attributes, descriptors;
- `src/Engines/GenerateDocService.Engine.Scriban`
  - Scriban document engine and template parser;
- `src/Engines/GenerateDocService.Engine.DotLiquid`
  - DotLiquid document engine and template parser;
- `src/Engines/GenerateDocService.Engine.QuestPdf`
  - JSON-to-PDF engine.
- `src/Engines/GenerateDocService.Engine.Fop`
  - HTTP adapter for FO/XSL-FO-to-PDF conversion through the external FOP Converter API.
- `src/Engines/GenerateDocService.Engine.QuestPdf`
  - JSON-to-PDF generation with QuestPDF.
- `src/Engines/GenerateDocService.Engine.MiniExcel`
  - XLSX generation and template population with MiniExcel.
- `src/Engines/GenerateDocService.Engine.MiniWord`
  - DOCX template population with the Open XML SDK.
- `src/Engines/GenerateDocService.Engine.MiniPdf`
  - XLSX-to-PDF conversion using MiniExcel and PDF rendering libraries.

## 2.4 Tests

- `tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests`
  - retains inspectable output from every in-process engine in `artifacts/test-results/<engine>/` for local inspection;
  - constructs TXT, HTML, Markdown, XLSX, and DOCX templates with JSON fixture data; XLSX and DOCX tests inspect Open XML parts to confirm populated values and remove placeholder tokens;
  - normalizes prefixed SpreadsheetML produced by MiniExcel before MiniPdf conversion so worksheet values are rendered;
- `tests/Integration/GenerateDocService.Api.IntegrationTests`
  - runs an opt-in Scriban XSL-FO/XML template-to-FOP converter test when `FOP_CONVERTER_BASE_URL` is configured and writes its PDF to `artifacts/test-results/fop/`;
- `tests/Performance/GenerateDocService.LoadTests.NBomber`

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

- DI registration;
- Redis cache and dedup implementations;
- MinIO artifact storage;
- MassTransit bus integration;
- EF Core PostgreSQL persistence, migrations, and advisory-locked schema application at startup;
- health checks;
- assembly scanning for engines and template parsers.
- configured `HttpClient` registration for the external FOP converter.

#### Presentation
Contains API contracts and mapping logic.

#### Host
Contains transport wiring:

- HTTP endpoints;
- middleware;
- logging;
- tracing;
- metrics export;
- OpenAPI.

### 3.2 Fault-tolerance model

The design already embeds several resilience patterns that keep generation jobs robust under transient failure:

- background generation retries transient faults (for example `HttpRequestException`, `TimeoutException`, `IOException`, `SocketException`, and Redis connection issues) with exponential backoff until the configured deadline is reached;
- the PostgreSQL outbox and task lease pattern ensures async work is not lost when the API or worker is restarted, and duplicate delivery is tolerated by the task state machine;
- the FOP converter uses a standard `HttpClient` resilience handler with per-attempt timeout, total timeout, retry windows, and a circuit breaker so converter outages do not cascade into unbounded wait states;
- generated document and request deduplication caches reduce repeated work and help the system recover from transient duplicate submissions without running the same job repeatedly;
- in-memory providers enforce TTL and bounded capacity so local development remains safe under load and does not leak memory indefinitely;
- health checks cover engine readiness, Redis, PostgreSQL, RabbitMQ, and object storage so operational issues are visible before they affect user requests.

This is not a full distributed-failure model yet, but it represents a solid fault-tolerant foundation for a modular monolith and gives the project a practical path toward a more production-grade deployment.

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
- if request `engine` is missing, the highest-priority matching engine is chosen.

### Current engine priorities

Based on current code:

- `scriban`: priority 100
- `dotliquid`: priority 90
- `questpdf`: own PDF capability
- `fop`: priority 300 for FO/XSL-FO-to-PDF conversion through the external converter
- `fake`: smoke and fallback/testing scenarios

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

Behavior:

- API accepts async request;
- request is normalized and enriched with correlation metadata;
- dedup fingerprint is created;
- task and serialized generation message are committed together when PostgreSQL is selected;
- a PostgreSQL outbox dispatcher claims pending messages with a lease and sends them through MassTransit;
- dispatch failures retry with backoff and eventually move the task to `Failed`;
- background processor performs generation and artifact save;
- task status is updated and lifecycle events are published best-effort.

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

| Technology | Where it is used | How it is used |
|------------|-----------------|----------------|
| Redis (`StackExchange.Redis`) | Infrastructure caching adapters | Replaces `ICompiledTemplateCache`, `IGeneratedDocumentCache`, and `IGenerationRequestDeduplicationStore`. Compiled templates and completed document references are best-effort caches; a transient cache outage is treated as a miss. Request-deduplication reservations remain fail-closed to avoid cross-instance duplicate work. |
| MinIO (`Minio` .NET client) | `MinioDocumentArtifactStore` | Implements `IDocumentArtifactStore`. The worker writes generated files to the configured bucket under `{requestId}/{fileName}`; task records hold the resulting storage path, and the download endpoint reads the artifact through the same abstraction. |
| MassTransit | Infrastructure messaging adapters | The PostgreSQL outbox dispatcher sends `GenerateDocumentRequested` to the configured RabbitMQ queue. A conditional database lease coordinates dispatchers in API/Worker hosts; the pending payload is cleared after broker acceptance. In-memory persistence submits directly. Generated/failed lifecycle events are published through `MassTransitDocumentGenerationEventPublisher`. |

These adapters are not mandatory in every run. `appsettings.json` and Development settings select in-memory caching, messaging, storage, and persistence by default. `deploy/docker/docker-compose.yml` configures Redis, RabbitMQ through MassTransit, MinIO, and PostgreSQL for both API and Worker. PostgreSQL stores task state and the pending command payload atomically; Redis does not replace the task repository. The outbox is at-least-once, so the consumer claims a task atomically and ignores duplicate deliveries after a task has left `Queued`.

---

## 5. Runtime call flows

## 5.1 Sync request flow

```text
Client
  -> POST /api/v1/documents/sync
  -> Program.cs maps HTTP contract to GenerateDocumentRequest
  -> CorrelationIdMiddleware injects correlation metadata
  -> SyncDocumentGenerationService
       -> ResolveRequest()
            -> explicit engine OR auto-select by capability/priority
       -> registry.Resolve(engine)
       -> engine.GenerateAsync(request)
            -> template parser registry resolves parser if needed
            -> compiled template cache may be used
       -> DocumentGenerationMetrics record success/failure
  -> API returns file directly
```

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

The sequence diagram for the Compose configuration (RabbitMQ, Redis, MinIO, and PostgreSQL) is in [async-generation-sequence.mmd](async-generation-sequence.mmd). The [MiniExcel async sequence](miniexcel-async-sequence.mmd) details the `json -> xlsx` path, including outbox dispatch, template and cache branches. With the default in-memory providers, the same application abstractions are used without those external services or the PostgreSQL outbox.

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
  -> choose parser from ITemplateParserRegistry
  -> hash template content
  -> try ICompiledTemplateCache
  -> if miss: compile template and cache it
  -> render against JSON payload model
  -> return GeneratedDocumentResult
```

---

## 6. Infrastructure configuration model

The application can run in lightweight mode or more production-like mode.

### 6.1 Caching providers

- InMemory
- Redis (`ICompiledTemplateCache`, `IGeneratedDocumentCache`, and `IGenerationRequestDeduplicationStore`)

### 6.2 Messaging transports

- InMemory
- RabbitMQ via MassTransit (`GenerateDocumentRequested` queue and generation lifecycle events)

### 6.3 Artifact storage providers

- InMemory
- ObjectStorage via MinIO (`IDocumentArtifactStore`)

### 6.4 Local docker stack

`deploy/docker/docker-compose.yml` starts:

- API and Worker
- RabbitMQ
- Redis
- MinIO
- PostgreSQL

The stack configures both API and Worker to use these external providers. The FOP converter is external to this Compose file and is configured separately.

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

Current custom metrics:

- `document_generation_sync_requests_total`
- `document_generation_async_requests_total`
- `document_generation_completed_total`
- `document_generation_failed_total`
- `document_generation_duration_ms`

### 7.4 Health checks

- engines;
- Redis connectivity when enabled;
- RabbitMQ endpoint readiness when enabled;
- object storage endpoint readiness when enabled.

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

## 8.2 What is already production-oriented

- correlation propagation;
- deduplication;
- object storage abstraction;
- bus-based async processing;
- health readiness split;
- metrics and tracing;
- outbox-backed async dispatch and bounded retry patterns;
- fault-tolerant generation lifecycle with retry deadlines and circuit breaking for external HTTP dependencies;
- integration coverage for core API behavior.

---

## 9. Current limitations

1. There is only one bounded context so far.
2. API security/authentication is not present.
3. Governance for large templates/documents is limited.
4. No persistent relational task store yet.
5. No formal versioning strategy for engine contracts.
6. No tenant isolation model.
7. Redis, RabbitMQ, and MinIO clients still need explicit per-client resilience policies beyond the generation workflow itself.
8. No explicit admin/operations endpoints beyond health and task status.

---

## 10. Recommended target architecture direction

Short term, keep the modular monolith.

Reason:

- current size does not justify distributed decomposition;
- boundaries are becoming clear but are still evolving;
- keeping a single deployable preserves development speed.

Medium term, evolve toward:

- dedicated durable task persistence;
- stronger configuration and secret handling;
- separate worker host for background generation if throughput requires it;
- stronger artifact lifecycle management;
- engine sandboxing/isolation for high-risk converters.

Long term, split only the components that justify independent scaling:

- API gateway/ingress;
- background workers;
- heavyweight conversion engines;
- reporting/analytics pipeline.

---

## 11. Architecture diagrams

## 11.1 High-level component diagram

```text
+-------------------+        +------------------------------+
|   Sample Client   | -----> |   GenerateDocService.Api     |
+-------------------+        |  - Minimal API endpoints     |
                             |  - Correlation middleware    |
                             |  - Health/Telemetry          |
                             +---------------+--------------+
                                             |
                                             v
                             +------------------------------+
                             | DocumentProcessing.Application|
                             | - Sync/Async services         |
                             | - Background processor        |
                             | - Registries                  |
                             | - Metrics                     |
                             +------+-----------+------------+
                                    |           |            |
                 +------------------+           |            +------------------+
                 v                              v                               v
+-------------------------------+   +--------------------------+   +-----------------------------+
| Engine Registry / Parsers     |   | Messaging / Tasks        |   | Cache / Artifact Storage    |
| - Scriban                     |   | - MassTransit            |   | - InMemory / Redis          |
| - DotLiquid                   |   | - InMemory / RabbitMQ    |   | - InMemory / MinIO          |
| - QuestPDF                    |   | - Task repository        |   | - Dedup / generated cache   |
+-------------------------------+   +--------------------------+   +-----------------------------+
```

## 11.2 Sync sequence diagram

```text
Client -> API -> SyncDocumentGenerationService -> EngineRegistry -> Engine -> ParserRegistry/TemplateCache -> API -> Client
```

## 11.3 Async sequence diagram

```text
Client -> API -> AsyncDocumentGenerationService -> DedupStore -> TaskRepository -> Scheduler/Bus
Bus -> BackgroundDocumentGenerationProcessor -> EngineRegistry -> Engine -> ArtifactStore -> TaskRepository -> EventPublisher
Client -> TaskStatus API -> Download API
```

---

## 12. Conclusion

The project is in a strong intermediate state:

- more mature than a scaffold;
- still simple enough to evolve safely;
- architecturally sound for iterative hardening.

The best next steps are not a rewrite, but targeted improvements in persistence, security, resilience, governance, and operational maturity.
