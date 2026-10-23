# GenerateDocService

## Overview

Initial Clean Architecture scaffold for a high-load document generation and transformation service on .NET.

Current first iteration includes:

- solution and project structure for Domain, Application, Infrastructure, Presentation, Host, and Client;
- engine abstractions;
- first real engine module based on Scriban templates;
- second template engine based on DotLiquid;
- QuestPDF PDF generation engine scaffold;
- Apache FOP-compatible XSL-FO-to-PDF conversion through an external HTTP service;
- template parser registry for pluggable template providers;
- in-memory compiled template cache foundation for template parsers;
- Redis-backed compiled template cache support;
- metadata-driven engine registry and task repository;
- automatic engine selection by capability and priority when `engine` is omitted;
- async messaging contracts and MassTransit-based background processing pipeline scaffold;
- in-memory deduplication and generated document cache abstractions ready for Redis replacement;
- infrastructure options for Redis-backed cache/deduplication and object storage providers;
- MinIO-based object storage adapter and local Docker Compose environment;
- liveness and readiness health checks for engines and infrastructure dependencies;
- correlation id middleware for HTTP entry points and async pipeline propagation;
- Serilog console logging enriched with correlation id;
- OpenTelemetry tracing for ASP.NET Core, outgoing HTTP calls, and MassTransit activity source;
- custom OpenTelemetry metrics for sync requests, async requests, completions, failures, and generation duration;
- OpenAPI metadata for current endpoints;
- demo `fake` generation engine;
- sync and async HTTP endpoints;
- sample .NET client;
- NBomber-based performance baseline project;
- unit and integration test coverage.

## Documentation

- Architecture and call-flow diagrams: `docs/architecture.md`
- Improvement plan and roadmap: `docs/improvement-plan.md`

## Projects

- `src/Engines/GenerateDocService.Engine.Abstractions`
- `src/Engines/GenerateDocService.Engine.Scriban`
- `src/Engines/GenerateDocService.Engine.DotLiquid`
- `src/Engines/GenerateDocService.Engine.QuestPdf`
- `src/Engines/GenerateDocService.Engine.Fop`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Domain`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation`
- `src/Host/GenerateDocService.Api`
- `src/Clients/GenerateDocService.SampleClient`
- `tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests`
- `tests/Integration/GenerateDocService.Api.IntegrationTests`
- `tests/Performance/GenerateDocService.LoadTests.NBomber`

## Engine registration

Document generation engines are registered as plugins:

- each engine implements `IDocumentGenerationEngine`;
- each engine is annotated with `DocumentEngineAttribute` metadata;
- infrastructure scans assemblies and registers engines in DI;
- application registry exposes both runtime instances and descriptors for routing.

Current engines:

- `fake` for simple scaffolding and smoke flows;
- `scriban` for template-driven text, markdown, html, and json generation from JSON payloads;
- `dotliquid` for template-driven text, markdown, html, and json generation from JSON payloads;
- `questpdf` for JSON-to-PDF generation;
- `miniexcel` for JSON-to-XLSX (Excel) generation from JSON arrays or objects;
- `miniword` for template-driven .docx generation from JSON payloads using `{{tag}}` syntax;
- `minipdf` for .docx/.xlsx-to-PDF conversion (payload is base64-encoded source document).
- `fop` for FO/XSL-FO-to-PDF conversion through the external FOP Converter API.

This keeps the current modular monolith ready for future extraction of engines into isolated modules or services.

## Async pipeline foundation

The async flow now uses explicit message contracts and processing abstractions:

- `GenerateDocumentRequested`
- `DocumentGenerated`
- `DocumentGenerationFailed`

Background processing runs in a dedicated worker host:

- `src/Host/GenerateDocService.Worker` — dedicated background processing service
- Worker consumes `GenerateDocumentRequested` messages from the message bus
- Worker shares the same Application and Infrastructure layers as the API
- API and Worker can scale independently

Current implementation uses an in-memory scheduler and artifact store, but the application layer is now shaped so MassTransit, Redis, and S3/MinIO can replace these implementations without changing the API contract or orchestration flow.

Infrastructure now hosts MassTransit directly:

- default transport: `InMemory`
- production-ready transport option: `RabbitMQ`
- consumer: `GenerateDocumentRequestedConsumer`
- scheduler publishes `GenerateDocumentRequested` through the bus
- result events are published as `DocumentGenerated` / `DocumentGenerationFailed`

## Caching and deduplication foundation

The application layer now separates two concerns that will later move to Redis:

- request deduplication by deterministic fingerprint;
- generated document cache by fingerprint and artifact reference.

Template parsers now also support compiled template caching through `ICompiledTemplateCache`, with an in-memory implementation already wired for Scriban.

When Redis caching is enabled, compiled templates can also be stored in Redis through the same provider model.

Artifact storage now returns a `DocumentArtifactReference` instead of a raw string path, which makes the abstraction ready for MinIO/S3 style providers.

Infrastructure configuration now supports:

- `DocumentProcessing:Persistence:Provider = InMemory | PostgreSql`
- `DocumentProcessing:Caching:Provider = InMemory | Redis`
- `DocumentProcessing:Messaging:Transport = InMemory | RabbitMQ`
- `DocumentProcessing:Storage:Provider = InMemory | ObjectStorage`

Task persistence now supports a PostgreSQL-backed EF Core repository for durable async task storage.

Redis adapters for generated document cache, request deduplication, and compiled templates are implemented and can be enabled through configuration.

Object storage support now includes a MinIO implementation of `IDocumentArtifactStore`.

The FOP adapter calls `POST /api/v1/convert/sync` on the FOP Converter API with a multipart `primaryDocument` part. Configure its base URL with `DocumentProcessing:Fop:BaseUrl`; it defaults to `http://localhost:8080/` and its OpenAPI document is available at `/v3/api-docs`. Run the converter on a different host or port when the GenerateDocService API itself is mapped to port 8080.

## Local infrastructure

Local production-like dependencies are defined in:

- `deploy/docker/docker-compose.yml`
- `src/Host/GenerateDocService.Api/Dockerfile`
- `src/Host/GenerateDocService.Worker/Dockerfile`

The compose stack starts:

- **API** (`generatedocservice-api`) — HTTP endpoints on port 8080
- **Worker** (`generatedocservice-worker`) — background processing
- **RabbitMQ** — message broker (ports 5672, 15672 for management UI)
- **Redis** — caching and deduplication (port 6379)
- **MinIO** — object storage (ports 9000, 9001 for console)
- **PostgreSQL** — durable task persistence (port 5432)

### Quick start with Docker

```powershell
# Build and start all services
docker compose -f .\deploy\docker\docker-compose.yml up --build

# Or run in detached mode
docker compose -f .\deploy\docker\docker-compose.yml up --build -d
```

### Access services

| Service | URL | Credentials |
|---------|-----|-------------|
| **API** | http://localhost:8080 | — |
| **Scalar API Reference** | http://localhost:8080/scalar/v1 | — |
| **OpenAPI JSON** | http://localhost:8080/openapi/v1.json | — |
| **RabbitMQ Management** | http://localhost:15672 | guest / guest |
| **MinIO Console** | http://localhost:9001 | minioadmin / minioadmin |
| **PostgreSQL** | localhost:5432 | postgres / postgres |

### Example API requests

**List engines:**
```powershell
curl http://localhost:8080/api/v1/engines
```

**Generate document synchronously (MiniExcel):**
```powershell
curl -X POST http://localhost:8080/api/v1/documents/sync `
  -H "Content-Type: application/json" `
  -d '{
    "engine": "miniexcel",
    "inputFormat": "json",
    "outputFormat": "xlsx",
    "payload": "[{\"Name\": \"Alice\", \"Age\": 30}, {\"Name\": \"Bob\", \"Age\": 25}]"
  }'
```

**Generate document synchronously (FOP):**
```powershell
curl -X POST http://localhost:8080/api/v1/documents/sync `
  -H "Content-Type: application/json" `
  -d '{
    "engine": "fop",
    "inputFormat": "xsl-fo",
    "outputFormat": "pdf",
    "payload": "<fo:root xmlns:fo=\"http://www.w3.org/1999/XSL/Format\"><fo:layout-master-set><fo:simple-page-master master-name=\"default\" page-height=\"11in\" page-width=\"8.5in\"><fo:region-body/></fo:simple-page-master></fo:layout-master-set><fo:page-sequence master-reference=\"default\"><fo:flow flow-name=\"xsl-region-body\"><fo:block>Hello from FOP</fo:block></fo:flow></fo:page-sequence></fo:root>"
  }' --output document.pdf
```

**Generate document asynchronously:**
```powershell
curl -X POST http://localhost:8080/api/v1/documents/async `
  -H "Content-Type: application/json" `
  -d '{
    "engine": "scriban",
    "inputFormat": "json",
    "outputFormat": "txt",
    "templateFormat": "scriban",
    "payload": "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
    "template": "Document: {{ document }} for {{ customer.name }}"
  }'
```

**Check task status:**
```powershell
curl http://localhost:8080/api/v1/tasks/{taskId}
```

**Download generated document:**
```powershell
curl http://localhost:8080/api/v1/tasks/{taskId}/download --output document.xlsx
```

### Stop services

```powershell
# Stop all services
docker compose -f .\deploy\docker\docker-compose.yml down

# Stop and remove volumes (including PostgreSQL data)
docker compose -f .\deploy\docker\docker-compose.yml down -v
```

### View logs

```powershell
# All services
docker compose -f .\deploy\docker\docker-compose.yml logs -f

# Specific service
docker compose -f .\deploy\docker\docker-compose.yml logs -f generatedocservice-api
```

## Request validation

API endpoints now enforce configurable guardrails to prevent resource abuse:

- **Payload size**: default 10 MB
- **Template size**: default 5 MB
- **Metadata**: max 50 entries, key length 128, value length 1024
- **Format/engine name lengths**: default 64 characters
- **Request ID length**: default 64 characters

Invalid requests return `400 Bad Request` with field-level error details.

Configure limits via `DocumentProcessing:Validation` in `appsettings.json`.

## Authentication and authorization

API supports JWT bearer authentication with role-based authorization:

- **Disabled by default** — local development requires no tokens
- **Enable** by setting `DocumentProcessing:Authentication:Enabled = true`
- **Roles**: `DocumentSubmit`, `DocumentRead`, `DocumentDownload`, `DocumentAdmin`
- **Endpoint mapping**:
  - `POST /documents/*` → `DocumentSubmit` or `DocumentAdmin`
  - `GET /engines/*`, `GET /tasks/{taskId}` → `DocumentRead` or `DocumentAdmin`
  - `GET /tasks/{taskId}/download` → `DocumentDownload` or `DocumentAdmin`

Configure JWT settings via `DocumentProcessing:Authentication` (Issuer, Audience, SigningKey).

## Artifact retention

Stored artifacts and task records are automatically cleaned up by a background service:

- **Completed tasks**: deleted after `RetentionDays` (default: 30 days)
- **Failed tasks**: deleted after `FailedTaskRetentionDays` (default: 7 days)
- **Cleanup interval**: runs every `CleanupIntervalHours` (default: 1 hour)

Configure via `DocumentProcessing:Retention` in `appsettings.json`.

## API

Current endpoints:

- `GET /health/live`
- `GET /health/ready`
- `GET /api/v1/engines`
- `GET /api/v1/engines/{name}`
- `POST /api/v1/documents/sync`
- `POST /api/v1/documents/async`
- `GET /api/v1/tasks/{taskId}`
- `GET /api/v1/tasks/{taskId}/download`

Health checks cover:

- registered document engines;
- Redis connectivity when Redis provider is enabled;
- RabbitMQ TCP readiness when RabbitMQ transport is enabled;
- MinIO/object storage TCP readiness when object storage provider is enabled.

Correlation id:

- request header: `X-Correlation-Id`
- if the header is not provided, the API generates one automatically;
- the value is returned in the response header;
- the same value is propagated into async message metadata as `correlationId`.

Observability:

- Serilog writes structured logs to the console;
- `CorrelationId` is pushed into the logging scope by middleware;
- OpenTelemetry tracing is enabled for ASP.NET Core requests and outgoing HTTP calls;
- the host also listens to the `MassTransit` activity source;
- OTLP export can be enabled with `OpenTelemetry__Tracing__OtlpEndpoint`;
- custom metrics are emitted from meter `GenerateDocService.DocumentProcessing`.

Current custom metrics:

- `document_generation_sync_requests_total`
- `document_generation_async_requests_total`
- `document_generation_completed_total`
- `document_generation_failed_total`
- `document_generation_duration_ms`

All custom metrics include tags:

- `engine`
- `output_format`

Example request body:

```json
{
  "requestId": "optional-request-id",
  "engine": "scriban",
  "inputFormat": "json",
  "outputFormat": "txt",
  "templateFormat": "scriban",
  "payload": "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
  "template": "Document: {{ document }} for {{ customer.name }}",
  "metadata": {
    "client": "sample"
  }
}
```

`engine` can be omitted for auto-selection. In that case the application chooses the highest-priority registered engine that can handle the requested `inputFormat`, `outputFormat`, and `templateFormat`.

Example async status response:

```json
{
  "taskId": "d8f45ab3f8994fd68f1f1c84263dc8f0",
  "status": 2,
  "resultFileName": "d8f45ab3f8994fd68f1f1c84263dc8f0.txt",
  "resultStoragePath": "artifacts/d8f45ab3f8994fd68f1f1c84263dc8f0/d8f45ab3f8994fd68f1f1c84263dc8f0.txt",
  "downloadUrl": "https://localhost:7001/api/v1/tasks/d8f45ab3f8994fd68f1f1c84263dc8f0/download",
  "error": null,
  "createdAtUtc": "2026-01-01T12:00:00Z",
  "updatedAtUtc": "2026-01-01T12:00:01Z"
}
```

When `status` is `Completed`, the client can use `downloadUrl` to retrieve the generated artifact.

## Testing

Unit tests cover:

- engine registry behavior;
- engine generation behavior;
- correlation middleware;
- async enqueue flow;
- artifact download service;
- custom metrics helpers.

Successful engine tests retain inspectable output in `artifacts/test-results/<engine>/`:

- `scriban` and `dotliquid` render JSON data into TXT, HTML, and Markdown templates; `fake` writes smoke-test text output;
- `miniexcel` populates an XLSX template and `miniword` populates DOCX placeholders; tests inspect the generated workbook/document XML to ensure values are rendered and template tokens are absent;
- `questpdf` and `minipdf` write PDFs. The MiniPdf test confirms spreadsheet values are rendered, including XLSX files produced by MiniExcel.
- `fop` renders a Scriban XSL-FO/XML template before its opt-in integration test submits the populated FO to the external converter and writes `fop-template-generated.pdf`.

The folder is ignored by Git and refreshed by the test suite. FOP also has a mock HTTP contract test. To generate its real artifact with a local converter:

```powershell
$env:FOP_CONVERTER_BASE_URL = "http://localhost:8080/"
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj --filter "FullyQualifiedName~FopDocumentGenerationEngineIntegrationTests"
```

Integration tests currently cover:

- engine discovery endpoint;
- sync generation endpoint;
- async generation endpoint;
- task polling;
- artifact download endpoint;
- sync and async auto-selection when `engine` is omitted;
- correlation id header propagation through HTTP responses.

Run unit tests:

```powershell
dotnet test tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests/GenerateDocService.DocumentProcessing.Application.Tests.csproj
```

Run integration tests:

```powershell
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj
```

## Performance baseline

The repository includes an NBomber-based baseline project:

- `tests/Performance/GenerateDocService.LoadTests.NBomber`

Current profiles support:

- `sync_scriban_explicit`
- `async_scriban_explicit`
- `sync_scriban_auto`
- `async_scriban_auto`
- `sync_scriban_warm`
- `sync_scriban_cold`
- `sync_dotliquid_explicit`
- `async_dotliquid_explicit`
- `sync_dotliquid_auto`
- `async_dotliquid_auto`
- `sync_dotliquid_warm`
- `sync_dotliquid_cold`
- `sync_questpdf`

The first CLI argument is the API base URL.
The second CLI argument selects the sync profile.
The third CLI argument selects the async profile.

Example: compare explicit Scriban routing against auto-selection routing:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_explicit async_scriban_auto
```

Example: run PDF baseline together with explicit async Scriban:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_questpdf async_scriban_explicit
```

Example: compare warm vs cold template-cache path for Scriban:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_warm async_scriban_explicit
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_cold async_scriban_explicit
```

Example: compare Scriban and DotLiquid on the same sync text-generation flow:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_explicit async_dotliquid_explicit
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_dotliquid_explicit async_scriban_explicit
```

By default, NBomber reports are written to:

- `artifacts/perf/<timestamp>-<sync-profile>-<async-profile>`

You can override the report root with a fourth CLI argument:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_warm async_scriban_explicit .\artifacts\perf
```

Recommended baseline comparison workflow:

1. Run one warm-cache baseline.
2. Run one cold-cache baseline.
3. Compare Scriban and DotLiquid using the same input/output shape.
4. Keep report folders as historical snapshots for regression comparison.

## Run

Start API:

```powershell
dotnet run --project src/Host/GenerateDocService.Api
```

Run sample client:

```powershell
dotnet run --project src/Clients/GenerateDocService.SampleClient -- https://localhost:7001
```

## Validation

Build:

```powershell
dotnet build .\GenerateDocService.slnx
```

Test:

```powershell
dotnet test tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests/GenerateDocService.DocumentProcessing.Application.Tests.csproj
```

## Copilot skills

Repository guidance for Copilot is stored in:

- `.github/copilot-instructions.md`
- `.github/skills/project-bootstrap/SKILL.md`
- `.github/skills/documentation-work/SKILL.md`
- `.github/skills/service-design/SKILL.md`
- `.github/skills/document-generation/SKILL.md`
- `.github/skills/testing/SKILL.md`
- `.github/skills/api-service/SKILL.md`
- `.github/skills/senior-architect/SKILL.md`
- `.github/skills/csharp-developer/SKILL.md`
<!--  -->