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

## 2.4 Tests

- `tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests`
- `tests/Integration/GenerateDocService.Api.IntegrationTests`
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
- health checks;
- assembly scanning for engines and template parsers.

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
- task is reserved and created;
- message is scheduled through MassTransit;
- background processor performs generation and artifact save;
- task status is updated and events are published.

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
       -> create task record
       -> enqueue GenerateDocumentRequested through scheduler
  -> API returns 202 Accepted + task status payload

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
- Redis

### 6.2 Messaging transports

- InMemory
- RabbitMQ via MassTransit

### 6.3 Artifact storage providers

- InMemory
- ObjectStorage via MinIO

### 6.4 Local docker stack

`deploy/docker/docker-compose.yml` starts:

- API
- RabbitMQ
- Redis
- MinIO

This is a practical local environment for integration and baseline testing.

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
- integration coverage for core API behavior.

---

## 9. Current limitations

1. There is only one bounded context so far.
2. API security/authentication is not present.
3. Governance for large templates/documents is limited.
4. No persistent relational task store yet.
5. No formal versioning strategy for engine contracts.
6. No tenant isolation model.
7. No advanced resilience policies around external dependencies.
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
