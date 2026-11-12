# GenerateDocService

.NET 10 modular monolith for synchronous and asynchronous document generation.
Clean Architecture with a plugin engine model. This file is the short operational
summary; `docs/architecture.md` is the authoritative description and
`docs/improvement-plan.md` §11 is the task-level record of what is done and what
is still open.

## Commands

```powershell
# Build the whole solution
dotnet build .\GenerateDocService.slnx

# Unit tests (no infrastructure needed)
dotnet test tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests/GenerateDocService.DocumentProcessing.Application.Tests.csproj

# Integration tests (container tests skip cleanly when Docker is unreachable)
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj

# Only the container-backed tests
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj --filter "Category=RequiresDocker"

# Run the API (listens on http://localhost:5287 and https://localhost:7025)
dotnet run --project src/Host/GenerateDocService.Api

# Run the sample client (its default base address is the Docker stack, http://localhost:8081)
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287

# In-process microbenchmarks — Release is mandatory
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark

# Add an EF Core migration after changing DocumentGenerationDbContext
dotnet tool restore
dotnet ef migrations add <Name> --project src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure --output-dir Persistence/Migrations

# Docker stack (needs GDS_AUTH_SIGNING_KEY in deploy/docker/.env)
docker compose -f .\deploy\docker\docker-compose.yml up --build -d
```

A Release build that also enforces the NuGet advisory gate, as CI does:

```powershell
dotnet build .\GenerateDocService.slnx -c Release -p:WarningsAsErrors=NU1902%3BNU1903
```

The `%3B` escape is required. A literal `;` is split by the shell and MSBuild
rejects the argument with `MSB1006`.

## Layout

- `src/Modules/DocumentProcessing/*` — the single bounded context, split into
  `Domain`, `Application`, `Infrastructure`, `Presentation`.
- `src/Host/GenerateDocService.Api` — HTTP surface, validation, auth policies,
  health, telemetry.
- `src/Host/GenerateDocService.Worker` — background generation; shares
  Application and Infrastructure, publishes no HTTP surface.
- `src/Engines/*` — plugin engines plus `Engine.Abstractions`.
- `src/Clients/GenerateDocService.SampleClient` — interactive demo and the
  BenchmarkDotNet host.
- `tests/{Unit,Integration,Performance}` — xUnit suites and NBomber profiles.

## Conventions that are easy to get wrong

**Engines.** An engine is registered by assembly scanning only if it carries
`[DocumentEngine(name, InputFormats, OutputFormats, TemplateFormats, Priority)]`.
A new engine project without the attribute produces no engine and no error. Nine
engines exist: `fake` (priority 0, lives in Infrastructure), `api` (10),
`dotliquid` (90), `scriban` (100), `miniword` (100), `miniexcel` (150),
`questpdf` (200), `minipdf` (250), `fop` (300). Auto-selection picks highest
priority, then name ascending.

**Retry is a decision.** `TransientFailureClassifier` walks the inner-exception
chain and checks, in order: `NonRetryableGenerationException`, then cancellation,
then the transient types. An engine calling a third party whose idempotency it
cannot guarantee must throw `NonRetryableGenerationException` — the `api` engine
does, `fop` does not.

**Providers are switched with `services.Replace(...)`.**
`DocumentProcessing:{Persistence,Caching,Messaging,Storage}` each default to
`InMemory`, so the application layer never learns which implementation it got.
Do not add a direct dependency on Redis, RabbitMQ, MinIO or Npgsql from
Application or from an engine.

**MassTransit is pinned to 8.5.10.** Version 9 requires a paid licence. This is
not a routine dependency bump. With `InMemory` the scheduler *publishes*; with
`RabbitMQ` it *sends* to `queue:<QueueName>`, and the consumer's receive endpoint
must be bound to the same name or messages sit unconsumed.

**Tests use hand-rolled fakes.** xUnit plus FluentAssertions, no mocking library.
Do not add one. Container tests must use `[SkippableFact]` with
`[Trait("Category", "RequiresDocker")]` — `[Fact]` plus `Skip.If` reports a skip
as a hard failure when Docker is down. Inject `TimeProvider` wherever TTL or
expiry matters so tests stay deterministic.

**Validate configuration at startup.** A malformed setting should fail the host
with a clear message, not surface on the first request. `DocumentProcessingApiOptions.Validate()`
and the FOP resilience clamping are the existing examples.

**`DocumentProcessing:Api` is bound by two unrelated classes** — `ApiHostOptions`
in the API host (`ExposeOpenApi`, `ExposeHealthDetails`) and
`DocumentProcessingApiOptions` in the engine (`Targets`, `DefaultTarget`). They
read different keys from the same section.

**Secrets.** The JWT signing key has no committed default and the host fails fast
when auth is enabled without a key of at least 32 characters. `api` target
credentials belong in environment variables
(`DocumentProcessing__Api__Targets__<name>__Authentication__ClientSecret`), never
in `appsettings.json`, which is committed.

**Ports.** `dotnet run` serves the API on 5287. The Docker stack publishes 8081
(container 8080) and Redis on 6380 (container 6379), deliberately avoiding 8080
and 6379 so it can run beside the separate `fopconverter` project.

## Documentation

`README.md` / `README_ru.md` and `docs/architecture.md` / `docs/architecture_ru.md`
are paired. A factual change to one belongs in both. This repository's
documentation has drifted from the code before — verify claims against the source
rather than trusting the prose, and correct both languages when you find drift.
