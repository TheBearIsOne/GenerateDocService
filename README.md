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

## Architecture at a glance

`GenerateDocService` is organized as a modular monolith built around a clean separation of concerns:

- `src/Host/GenerateDocService.Api` exposes the HTTP surface, authentication, correlation IDs, health checks, OpenTelemetry, and API contracts.
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application` owns orchestration, engine selection, task lifecycle, deduplication, and generation workflows.
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure` wires caches, persistence, outbox dispatch, messaging, MinIO/object storage, and infrastructure-specific health checks.
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation` maps HTTP request/response models and keeps the API layer thin.
- `src/Engines/*` modules implement plugin-style document generation engines whose capabilities are discovered by metadata so the runtime can route requests without hard-coded coupling.
- `src/Host/GenerateDocService.Worker` handles asynchronous document generation, allowing work to be separated from the API ingress path when needed.

This keeps the solution easy to run locally while still allowing Redis, RabbitMQ, MinIO, and PostgreSQL to replace in-memory defaults without changing the core application contract.

## Fault tolerance and resiliency

Fault tolerance is built into the service design rather than treated as an afterthought:

- asynchronous generation uses a durable task and outbox model so requests are not lost when the API or worker restarts;
- transient broker, Redis, and generation failures are retried with bounded backoff and a processing deadline instead of looping forever;
- the FOP HTTP converter is protected by a standard resilience pipeline with per-attempt timeout, total timeout, exponential retry, and a circuit breaker;
- deduplication records and generated-document cache entries prevent duplicate work and short-circuit repeated generation for the same payload fingerprint;
- in-memory providers are bounded by TTL and capacity settings so they do not grow without limit under load;
- health checks and readiness probes surface infrastructure and engine problems before they become customer-visible outages.

The project is therefore designed for both developer ergonomics and production-minded failure handling, while still remaining modular enough to evolve into a more distributed deployment model later.

## Documentation

- Architecture and call-flow diagrams: `docs/architecture.md`
- Improvement plan and roadmap: `docs/improvement-plan.md`

## Projects

- `src/Engines/GenerateDocService.Engine.Abstractions`
- `src/Engines/GenerateDocService.Engine.Api`
- `src/Engines/GenerateDocService.Engine.Scriban`
- `src/Engines/GenerateDocService.Engine.DotLiquid`
- `src/Engines/GenerateDocService.Engine.QuestPdf`
- `src/Engines/GenerateDocService.Engine.MiniExcel`
- `src/Engines/GenerateDocService.Engine.MiniWord`
- `src/Engines/GenerateDocService.Engine.MiniPdf`
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
- `api` for calling a configured external HTTP API and returning its response as the generated document.

### MiniExcel payload rules

The table path (`template` omitted) writes one row per item and applies three rules:

- **Numbers are formatted invariantly.** The engine pins `CultureInfo.InvariantCulture`, so `1234.56` is written the same way regardless of the host's locale. Previously the template path used the ambient culture and produced `1234,56` on a `ru-RU` host.
- **Nested values are rejected.** A JSON object or array where a cell is expected throws `InvalidOperationException` naming the JSON path, for example `$.address`. Previously the CLR type name `System.Collections.Generic.Dictionary\`2[...]` was written into the cell and the request reported success, so a corrupt workbook shipped silently. Flatten nested data in the payload.
- **Ragged items are aligned.** Headers are the union of every item's keys and missing values become empty cells. Previously headers came from the first item only, so an item missing a key raised an unhandled `KeyNotFoundException` and an item adding one had it dropped.

The template path also supports **repeating rows**: put `{{items.name}}`, `{{items.qty}}` and so on side by side in one row of the template and bind `items` to a JSON array, and MiniExcel expands that row once per record, writing numbers as numeric cells. The syntax is `{{list.field}}` — a leading `$` is *not* part of it and is left in the cell as a literal. Sample client example 15 demonstrates an invoice built this way.

One MiniExcel limitation is accepted rather than fixed: on the *template* path a `{{placeholder}}` bound to a nested object or array raises an unhandled `IndexOutOfRangeException` from MiniExcel's renderer. The engine cannot reject nesting there without breaking the `{{list.field}}` collection syntax above, and cannot tell the two apart without parsing the template's tokens.

### The `api` engine

A generalisation of `fop`: it POSTs the payload to an external HTTP API and returns the response body as the document. Unlike `fop`, the endpoint and its credentials are **configuration, not request data**. A request may only name a pre-configured target, so the engine cannot be turned into an open proxy and no secret ever travels in a request body.

```jsonc
"DocumentProcessing": {
  "Api": {
    "DefaultTarget": "converter",
    "Targets": {
      "converter": {
        "BaseUrl": "http://localhost:8080/",
        "RequestPath": "api/v1/convert/sync?payloadType=XSL_FO&outputFormat=PDF&correlationId={correlationId}",
        "Method": "Post",
        "ContentType": "application/xml",
        "InputFormat": "xsl-fo",
        "OutputFormat": "pdf",
        "TimeoutSeconds": 120,
        "Authentication": { "Scheme": "None" }
      },
      "secure": {
        "BaseUrl": "https://api.example.com/",
        "RequestPath": "v1/documents",
        "InputFormat": "json",
        "OutputFormat": "pdf",
        "Authentication": {
          "Scheme": "OAuth2ClientCredentials",
          "TokenUrl": "https://id.example.com/connect/token",
          "ClientId": "gds",
          "ClientSecret": "set-this-through-configuration-not-source-control",
          "Scope": "documents.convert",
          "TokenExpirySkewSeconds": 30
        }
      }
    }
  }
}
```

A request selects a target with the `api.target` metadata key; without it the engine uses `DefaultTarget`, and failing that the first target by name whose declared formats match. `InputFormat` and `OutputFormat` on a target are optional — omit them and the target matches any format. `{requestId}` and `{correlationId}` in `RequestPath` are substituted per request.

The payload is sent as the raw request body with `ContentType`, unless `MultipartFieldName` is set — then it is sent as a `multipart/form-data` part under that name, which is what the FOP converter expects (`primaryDocument`). `Method` is `Post` (default) or `Put`.

`Authentication:Scheme` is one of:

| Scheme | Required fields | Header sent |
|--------|-----------------|-------------|
| `None` | — | none |
| `Basic` | `Username`, `Password` | `Authorization: Basic base64(user:pass)` |
| `Bearer` | `Token` | `Authorization: Bearer <token>` |
| `OAuth2ClientCredentials` | `TokenUrl`, `ClientId`, `ClientSecret`, optional `Scope` | `Authorization: Bearer <fetched token>` |

Client-credentials tokens are cached per token endpoint and client and refreshed `TokenExpirySkewSeconds` before expiry, so a busy target does not call the identity provider per document. Concurrent requests share a single in-flight token fetch.

Misconfigured targets throw at **startup**, not on the first request. There is deliberately **no retry at any layer**: the engine wraps upstream failures in `NonRetryableGenerationException`, so the background processor's transient-fault retry skips them as well. A configured target is an unknown third party whose endpoints may not be idempotent, and the request may already have been received before the failure, so an automatic retry could duplicate a side effect. Each target gets a `TimeoutSeconds` deadline instead, and the caller decides whether to resubmit.

The purpose-built `fop` engine is the opposite case: its converter is idempotent, so it keeps both the HTTP resilience pipeline from §Resilience and retries and the processor's transient retries.

Because capabilities come from configuration, the engine registers with empty format metadata — `/api/v1/engines` lists it with no formats, while auto-selection still resolves it correctly through `CanHandle`. Its priority is 10, so a purpose-built engine wins when both could serve a request.

A target named `converter` is preconfigured in `appsettings.Development.json` and in the compose stack, pointing at the FOP converter with the multipart shape it requires. It is what sample client example 16 drives, so the generic engine can be compared against the purpose-built `fop` engine against the same upstream. Remove it if you do not want the `api` engine to also claim `xsl-fo -> pdf`.

Credentials belong in environment variables, not in `appsettings.json`, which is committed — for example `DocumentProcessing__Api__Targets__secure__Authentication__ClientSecret`.

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
- with the `InMemory` transport the scheduler publishes `GenerateDocumentRequested` and MassTransit routes it to the consumer by convention
- with the `RabbitMQ` transport the scheduler *sends* it to the queue named by `DocumentProcessing:Messaging:QueueName` (default `document-generation`), and the consumer's receive endpoint is bound to that same name. The two must stay in sync — a mismatch leaves messages sitting in a queue with no consumers
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

The schema is versioned with EF Core migrations under `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure/Persistence/Migrations`. When the `PostgreSql` provider is selected, both the API and the worker apply pending migrations on startup; they serialise behind a PostgreSQL advisory lock so several hosts starting together cannot race on DDL. Nothing is applied for the `InMemory` provider.

To add a migration after changing `DocumentGenerationDbContext`:

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure --output-dir Persistence/Migrations
```

`dotnet-ef` is pinned as a local tool in `.config/dotnet-tools.json`, so `dotnet tool restore` is required once per clone. Scaffolding uses `DocumentGenerationDbContextFactory`, a design-time-only factory, which keeps `dotnet ef` from booting the API host.

Redis adapters for generated document cache, request deduplication, and compiled templates are implemented and can be enabled through configuration.

Object storage support now includes a MinIO implementation of `IDocumentArtifactStore`.

The FOP adapter calls `POST /api/v1/convert/sync` on the FOP Converter API with a multipart `primaryDocument` part. Configure its base URL with `DocumentProcessing:Fop:BaseUrl`; it defaults to `http://localhost:8080/` and the converter's OpenAPI document is available at `/v3/api-docs`. The converter is a separate stack, not part of `docker-compose.yml`.

That default only resolves when calling from the host — inside a container `localhost:8080` is the container itself. The compose stack therefore sets `DocumentProcessing__Fop__BaseUrl` on both the API and the worker to `${FOP_CONVERTER_BASE_URL:-http://host.docker.internal:8080/}` and adds the `host.docker.internal:host-gateway` host entry, so the `fop` engine reaches a converter published on the Docker host. Override `FOP_CONVERTER_BASE_URL` in `deploy/docker/.env` if the converter listens elsewhere or joins this compose network.

## Local infrastructure

Local production-like dependencies are defined in:

- `deploy/docker/docker-compose.yml`
- `src/Host/GenerateDocService.Api/Dockerfile`
- `src/Host/GenerateDocService.Worker/Dockerfile`

The compose stack starts:

- **API** (`generatedocservice-api`) — HTTP endpoints on host port **8081** (container port 8080), the only service published on all interfaces
- **Worker** (`generatedocservice-worker`) — background processing, no published ports
- **RabbitMQ** — message broker (5672, 15672 management UI)
- **Redis** — caching and deduplication (host port **6380**, container port 6379)
- **MinIO** — object storage (9000, 9001 console)
- **PostgreSQL** — durable task persistence (5432)

Host ports 8081 and 6380 are used instead of the conventional 8080 and 6379 so
the stack can run alongside the separate `fopconverter` project, which publishes
those two ports. Containers still address each other by service name over the
internal compose network (`redis:6379`), so the remapping is host-side only.

All infrastructure ports are bound to `127.0.0.1`, so they are reachable from the
host but not from the network. Containers talk to each other over the internal
compose network.

### Quick start with Docker

Authentication is **enabled** in the Docker stack, so a JWT signing key must be
supplied. Compose fails fast with a clear message if it is missing.

```powershell
# 1. Create the env file next to docker-compose.yml
copy .\deploy\docker\.env.example .\deploy\docker\.env

# 2. Put a random secret of at least 32 characters into GDS_AUTH_SIGNING_KEY
#    in .\deploy\docker\.env  (this file is gitignored)

# 3. Build and start all services
docker compose -f .\deploy\docker\docker-compose.yml up --build -d
```

### Access services

| Service | URL | Credentials |
|---------|-----|-------------|
| **API** | http://localhost:8081 | JWT required (see below) |
| **Scalar API Reference** | http://localhost:8081/scalar/v1 | — |
| **OpenAPI JSON** | http://localhost:8081/openapi/v1.json | — |
| **RabbitMQ Management** | http://localhost:15672 | guest / guest |
| **MinIO Console** | http://localhost:9001 | minioadmin / minioadmin |
| **PostgreSQL** | localhost:5432 | postgres / postgres |
| **Redis** | localhost:6380 | — |

Health endpoints (`/health/live`, `/health/ready`) stay anonymous so container
and orchestrator probes keep working.

### Getting a token

Tokens are HS256 JWTs signed with `GDS_AUTH_SIGNING_KEY`. Roles are read from the
standard `role` claim; `DocumentAdmin` satisfies every policy.

```powershell
function New-GdsToken {
    param(
        [string]$Key = $env:GDS_AUTH_SIGNING_KEY,
        [string]$Role = "DocumentAdmin",
        [int]$TtlMinutes = 60
    )

    function ConvertTo-Base64Url([byte[]]$bytes) {
        [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }

    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $payloadJson = @{
        iss = "GenerateDocService"
        aud = "GenerateDocService.Api"
        sub = "local-dev"
        role = $Role
        nbf = $now
        iat = $now
        exp = $now + ($TtlMinutes * 60)
    } | ConvertTo-Json -Compress
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))

    $hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Key))
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$payload")))

    "$header.$payload.$signature"
}
```

### Example API requests

All `/api/v1/*` calls need the bearer token when auth is enabled:

```powershell
$token = New-GdsToken
$auth = "Authorization: Bearer $token"
```

**List engines:**
```powershell
curl http://localhost:8081/api/v1/engines -H $auth
```

**Generate document synchronously (MiniExcel):**
```powershell
curl -X POST http://localhost:8081/api/v1/documents/sync `
  -H "Content-Type: application/json" -H $auth `
  -d '{
    "engine": "miniexcel",
    "inputFormat": "json",
    "outputFormat": "xlsx",
    "payload": "[{\"Name\": \"Alice\", \"Age\": 30}, {\"Name\": \"Bob\", \"Age\": 25}]"
  }'
```

**Generate document synchronously (FOP):**
```powershell
curl -X POST http://localhost:8081/api/v1/documents/sync `
  -H "Content-Type: application/json" -H $auth `
  -d '{
    "engine": "fop",
    "inputFormat": "xsl-fo",
    "outputFormat": "pdf",
    "payload": "<fo:root xmlns:fo=\"http://www.w3.org/1999/XSL/Format\"><fo:layout-master-set><fo:simple-page-master master-name=\"default\" page-height=\"11in\" page-width=\"8.5in\"><fo:region-body/></fo:simple-page-master></fo:layout-master-set><fo:page-sequence master-reference=\"default\"><fo:flow flow-name=\"xsl-region-body\"><fo:block>Hello from FOP</fo:block></fo:flow></fo:page-sequence></fo:root>"
  }' --output document.pdf
```

**Generate document asynchronously:**
```powershell
curl -X POST http://localhost:8081/api/v1/documents/async `
  -H "Content-Type: application/json" -H $auth `
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
curl http://localhost:8081/api/v1/tasks/{taskId} -H $auth
```

**Download generated document:**
```powershell
curl http://localhost:8081/api/v1/tasks/{taskId}/download -H $auth --output document.xlsx
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
- **Payload source**: exactly one of `payload` (text) or `payloadBase64` (binary) is required
- **Template source**: at most one of `template` or `templateBase64`
- **Base64 form**: strict — the standard alphabet, length a multiple of four, at most two trailing `=`, and no embedded whitespace

Size limits are applied to the **decoded** byte count for the base64 fields, so encoding cannot be used to exceed a limit. The validator measures that length arithmetically rather than decoding, so a request body is decoded once, in the mapper.

Invalid requests return `400 Bad Request` with field-level error details.

Configure limits via `DocumentProcessing:Validation` in `appsettings.json`.

## Authentication and authorization

API supports JWT bearer authentication with role-based authorization:

- **Disabled by default** when running with `dotnet run`, so local development needs no tokens
- **Enabled in the Docker stack** — `docker-compose.yml` sets `DocumentProcessing__Authentication__Enabled=true`
- **Roles**: `DocumentSubmit`, `DocumentRead`, `DocumentDownload`, `DocumentAdmin`
- **Endpoint mapping**:
  - `POST /documents/*` → `DocumentSubmit` or `DocumentAdmin`
  - `GET /engines/*`, `GET /tasks/{taskId}` → `DocumentRead` or `DocumentAdmin`
  - `GET /tasks/{taskId}/download` → `DocumentDownload` or `DocumentAdmin`
  - `GET /health/*` → anonymous, so probes keep working

Configure JWT settings via `DocumentProcessing:Authentication` (`Issuer`, `Audience`, `SigningKey`).

The signing key has **no committed default**. When authentication is enabled the
host fails fast at startup if `SigningKey` is empty or shorter than 32
characters, so a missing secret cannot silently degrade into a well-known key.
Supply it through configuration or the `GDS_AUTH_SIGNING_KEY` environment
variable (see `deploy/docker/.env.example`); never commit the real value.

## Artifact retention

Stored artifacts and task records are automatically cleaned up by a background service:

- **Completed tasks**: deleted after `RetentionDays` (default: 30 days)
- **Failed tasks**: deleted after `FailedTaskRetentionDays` (default: 7 days)
- **Abandoned tasks**: a task still `Processing` after `StuckProcessingTaskSeconds` (default: 1800) is moved to `Failed`, so a worker that crashed mid-generation cannot leave clients polling forever
- **Cleanup interval**: runs every `CleanupIntervalHours` (default: 1 hour)

Configure via `DocumentProcessing:Retention` in `appsettings.json`.

The cleanup policy lives in `ArtifactRetentionCleaner`; `ArtifactCleanupBackgroundService` is only its scheduler.

## Resilience and retries

Generation failures are classified before they are treated as final.

**Background processing** (`DocumentProcessing:Resilience`):

- **Processing timeout**: `ProcessingTimeoutSeconds` (default: 300) bounds a single generation. Exceeding it is terminal and fails the task with an explanatory error.
- **Bounded retry**: transient faults are retried up to `MaxAttempts` (default: 3) with exponential backoff between `InitialDelayMilliseconds` (250) and `MaxDelayMilliseconds` (5000), scaled by `BackoffMultiplier` (2.0).
- **Classification**: `TransientFailureClassifier` treats `HttpRequestException`, `TimeoutException`, `IOException`, and `SocketException` as retryable. Cancellation is always terminal, so an expired deadline is never retried into another full attempt. When the Redis provider is enabled the classifier also learns `RedisConnectionException`.
- `DocumentGenerationFailed` is published once, after the last attempt — not per retry.

**FOP converter client** (`DocumentProcessing:Fop`):

The external converter call runs through a standard resilience pipeline (timeout, retry, circuit breaker):

| Setting | Default | Purpose |
|---------|---------|---------|
| `AttemptTimeoutSeconds` | 30 | Per-attempt timeout |
| `TotalTimeoutSeconds` | 120 | Budget for the whole call including retries |
| `MaxRetryAttempts` | 2 | Retries after the first attempt |
| `RetryBaseDelayMilliseconds` | 1000 | Exponential backoff base |
| `CircuitBreakerMinimumThroughput` | 10 | Failures before the breaker can open |
| `CircuitBreakerSamplingDurationSeconds` | 60 | Breaker sampling window |
| `CircuitBreakerBreakDurationSeconds` | 30 | How long the breaker stays open |

`HttpStandardResilienceOptions` requires the sampling window to cover at least two attempt timeouts and the total budget to cover every attempt. Both are widened automatically if a hand-edited configuration violates that, so a bad value cannot turn the first FOP request into an `OptionsValidationException`.

**In-memory providers** (`DocumentProcessing:InMemory`):

The in-memory cache and deduplication stores are bounded so they match the Redis equivalents instead of growing for the lifetime of the process:

- `GeneratedDocumentTtlMinutes` (default: 60) and `MaxCachedDocuments` (default: 1000) — the oldest entries are evicted once over capacity
- `DeduplicationTtlMinutes` (default: 15) — without expiry, a task abandoned mid-flight would pin its fingerprint forever

Set a TTL to `0` to disable expiry.

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

### Developer surface exposure

`DocumentProcessing:Api` controls two things that are useful locally but leak implementation detail in production. Both are forced on by the Development environment, so `dotnet run` needs no configuration:

| Setting | Default | Effect when true |
|---------|---------|------------------|
| `ExposeOpenApi` | false | Serves `/openapi/v1.json` and the Scalar UI |
| `ExposeHealthDetails` | false | Returns per-check JSON from `/health/live` and `/health/ready` |

With `ExposeHealthDetails` false the probes answer with the bare overall status, which keeps engine implementation class names such as `ScribanDocumentGenerationEngine` away from anonymous callers. The compose stack runs `ASPNETCORE_ENVIRONMENT=Production` and sets `ExposeOpenApi=true` because Scalar is documented above; it leaves health detail off. Running as Production also removes the developer exception page, so an unhandled error no longer returns a stack trace with absolute source paths.

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

Binary payloads and templates use `payloadBase64` and `templateBase64` instead of the text fields. This is what makes `miniword`, `minipdf`, and the MiniExcel template path callable over HTTP, since a `.docx` or `.xlsx` cannot survive a UTF-8 string round trip:

```json
{
  "engine": "miniword",
  "inputFormat": "json",
  "outputFormat": "docx",
  "templateFormat": "docx",
  "payload": "{\"Name\":\"Ada Lovelace\",\"Score\":99}",
  "templateBase64": "UEsDBBQAAAAIAA...(base64 of the .docx template)...AA=="
}
```

Each pair is mutually exclusive: set `payload` **or** `payloadBase64`, and `template` **or** `templateBase64`. Sending both, or neither payload field, returns `400`. Binary input flows through the async pipeline unchanged, because `GenerateDocumentRequested` already carries `byte[]`.

`engine` can be omitted for auto-selection. In that case the application chooses the highest-priority registered engine that can handle the requested `inputFormat`, `outputFormat`, and `templateFormat`.

Example async status response:

```json
{
  "taskId": "d8f45ab3f8994fd68f1f1c84263dc8f0",
  "status": 2,
  "resultFileName": "d8f45ab3f8994fd68f1f1c84263dc8f0.txt",
  "resultStoragePath": "artifacts/d8f45ab3f8994fd68f1f1c84263dc8f0/d8f45ab3f8994fd68f1f1c84263dc8f0.txt",
  "downloadUrl": "http://localhost:5287/api/v1/tasks/d8f45ab3f8994fd68f1f1c84263dc8f0/download",
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
- background processing retries, the processing deadline, and terminal-failure handling;
- transient failure classification and retry backoff;
- in-memory cache and deduplication TTL and capacity bounds;
- artifact retention cleanup, including reaping tasks abandoned in `Processing`;
- resilience and FOP HTTP pipeline DI registration;
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
- correlation id header propagation through HTTP responses;
- JWT authentication and role-based authorization: 401 without a token, 403 for a role that does not satisfy the policy, 200 for a valid role, rejection of expired and wrongly-signed tokens, anonymous health probes, and startup failure when the signing key is shorter than 32 characters;
- the PostgreSQL task repository and the startup migration path, against a real container;
- a RabbitMQ async round-trip proving the consumer's receive endpoint matches `DocumentProcessing:Messaging:QueueName`;
- binary transport for `miniword`, `minipdf`, and the MiniExcel template path, including a base64 `.docx` template surviving the async pipeline and the base64 validation rejections.

Tests tagged `Category=RequiresDocker` start PostgreSQL and RabbitMQ containers through Testcontainers. They run automatically when a Docker daemon is reachable and skip cleanly when it is not, so a plain `dotnet test` works either way. Run only those with:

```powershell
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj --filter "Category=RequiresDocker"
```

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
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_explicit async_scriban_auto
```

Example: run PDF baseline together with explicit async Scriban:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_questpdf async_scriban_explicit
```

Example: compare warm vs cold template-cache path for Scriban:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_warm async_scriban_explicit
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_cold async_scriban_explicit
```

Example: compare Scriban and DotLiquid on the same sync text-generation flow:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_explicit async_dotliquid_explicit
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_dotliquid_explicit async_scriban_explicit
```

By default, NBomber reports are written to:

- `artifacts/perf/<timestamp>-<sync-profile>-<async-profile>`

You can override the report root with a fourth CLI argument:

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_warm async_scriban_explicit .\artifacts\perf
```

Recommended baseline comparison workflow:

1. Run one warm-cache baseline.
2. Run one cold-cache baseline.
3. Compare Scriban and DotLiquid using the same input/output shape.
4. Keep report folders as historical snapshots for regression comparison.

### In-process microbenchmarks

NBomber measures the service end to end over HTTP. For per-component numbers the sample client also hosts BenchmarkDotNet benchmarks that call the engines directly, with no API, Docker, or FOP converter involved:

```powershell
# Everything (takes a while; Release is required, BenchmarkDotNet rejects Debug)
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark

# One class, or one category
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --filter *TemplateParserCacheBenchmarks*
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --anyCategories text

# Smoke-check the harness without waiting for statistics
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --job Dry --filter *Scriban*
```

`benchmark` is also the `b` command in the interactive menu, and any arguments after it are passed straight to BenchmarkDotNet.

Three classes live in `src/Clients/GenerateDocService.SampleClient/Benchmarks/InProcessBenchmarks.cs`:

| Class | Categories | Measures |
|-------|-----------|----------|
| `EngineGenerationBenchmarks` | `text`, `office`, `pdf` | scriban, dotliquid, questpdf, and the miniexcel table and template paths plus miniword |
| `TemplateParserCacheBenchmarks` | `scriban`, `dotliquid` | template compilation on a cache miss against a warm `InMemoryCompiledTemplateCache` hit |
| `RequestPipelineBenchmarks` | `fingerprint`, `transport` | request fingerprinting at 1 KB and 1 MB, and base64 encode/decode of a 1 MB payload |

Every payload, template and request is built in `[GlobalSetup]`, so the measured methods allocate only what the engine itself allocates. Reports are written to `BenchmarkDotNet.Artifacts/results/`, which is gitignored.

These benchmarks do **not** cover `GenerateDocumentRequestValidator`, because it lives in the API host and referencing that project from the sample client would pull the whole web host into a demo executable.

## Run

Start API:

```powershell
dotnet run --project src/Host/GenerateDocService.Api
```

The API listens on `https://localhost:7025` and `http://localhost:5287` (see `Properties/launchSettings.json`).

Run sample client:

```powershell
# Interactive menu of engine examples
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287

# Non-interactive: one example, every example, or just the engine list
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287 7
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287 all
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287 engines
```

The menu covers all nine engines across sixteen examples: `fake`, `scriban` (text, HTML, Markdown, plus the full async submit/poll/download flow), `dotliquid`, `questpdf`, `miniexcel` (table path, template path, and an invoice template with repeating `{{items.*}}` data rows), `miniword` (a single-record form and a multi-paragraph invoice), `minipdf`, `fop`, `api` (the generic engine against the configured `converter` target), and two auto-selection examples that omit `engine`. Text output is printed inline; PDF, XLSX and DOCX output is written to `output/` beside the client binary. Menu commands are `0`/`all`, `e` (engines), `m` (menu), `q` (quit).

Set `GDS_SAMPLE_TOKEN` to attach a bearer token, which the Docker stack requires:

```powershell
$env:GDS_SAMPLE_TOKEN = New-GdsToken   # see "Getting a token" above
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:8081
```

The binary examples build their `.docx` and `.xlsx` fixtures in memory (`OfficeFixtures`), so the client is self-contained and needs no files on disk. The `minipdf` example chains two calls: it generates a workbook through `miniexcel` first, then feeds those bytes back as a base64 payload.

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