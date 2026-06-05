# GenerateDocService

## Overview

Initial Clean Architecture scaffold for a high-load document generation and transformation service on .NET.

Current first iteration includes:

- solution and project structure for Domain, Application, Infrastructure, Presentation, Host, and Client;
- engine abstractions;
- first real engine module based on Scriban templates;
- QuestPDF PDF generation engine scaffold;
- in-memory compiled template cache foundation for template parsers;
- metadata-driven engine registry and task repository;
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
- unit and integration test coverage.

## Projects

- `src/Engines/GenerateDocService.Engine.Abstractions`
- `src/Engines/GenerateDocService.Engine.Scriban`
- `src/Engines/GenerateDocService.Engine.QuestPdf`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Domain`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation`
- `src/Host/GenerateDocService.Api`
- `src/Clients/GenerateDocService.SampleClient`
- `tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests`
- `tests/Integration/GenerateDocService.Api.IntegrationTests`

## Engine registration

Document generation engines are registered as plugins:

- each engine implements `IDocumentGenerationEngine`;
- each engine is annotated with `DocumentEngineAttribute` metadata;
- infrastructure scans assemblies and registers engines in DI;
- application registry exposes both runtime instances and descriptors for routing.

Current engines:

- `fake` for simple scaffolding and smoke flows;
- `scriban` for template-driven text, markdown, html, and json generation from JSON payloads;
- `questpdf` for JSON-to-PDF generation.

This keeps the current modular monolith ready for future extraction of engines into isolated modules or services.

## Async pipeline foundation

The async flow now uses explicit message contracts and processing abstractions:

- `GenerateDocumentRequested`
- `DocumentGenerated`
- `DocumentGenerationFailed`

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

- `DocumentProcessing:Caching:Provider = InMemory | Redis`
- `DocumentProcessing:Messaging:Transport = InMemory | RabbitMQ`
- `DocumentProcessing:Storage:Provider = InMemory | ObjectStorage`

Redis adapters for generated document cache, request deduplication, and compiled templates are implemented and can be enabled through configuration.

Object storage support now includes a MinIO implementation of `IDocumentArtifactStore`.

## Local infrastructure

Local production-like dependencies are defined in:

- `deploy/docker/docker-compose.yml`
- `src/Host/GenerateDocService.Api/Dockerfile`

The compose stack starts:

- API
- RabbitMQ
- Redis
- MinIO

Run locally:

```powershell
docker compose -f .\deploy\docker\docker-compose.yml up --build
```

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

Integration tests currently cover:

- engine discovery endpoint;
- async generation endpoint;
- task polling;
- artifact download endpoint;
- correlation id header propagation through HTTP responses.

Run unit tests:

```powershell
dotnet test tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests/GenerateDocService.DocumentProcessing.Application.Tests.csproj
```

Run integration tests:

```powershell
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj
```

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
