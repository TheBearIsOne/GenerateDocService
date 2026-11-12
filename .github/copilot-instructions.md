# Copilot instructions for GenerateDocService

## Project context
- .NET 10 modular monolith for synchronous and asynchronous document generation, structured as Clean Architecture: `Domain`, `Application`, `Infrastructure`, `Presentation`, two hosts (`Api`, `Worker`), a sample client, and pluggable engine projects under `src/Engines/`.
- The stack is defined and stable — do not treat this as an empty scaffold or propose a structure. Read `docs/architecture.md` before changing boundaries, and `docs/improvement-plan.md` §11 for what is already done versus still open.
- Four infrastructure providers are switchable by configuration (`DocumentProcessing:{Persistence,Caching,Messaging,Storage}`), each with an in-memory default so `dotnet run` works with nothing installed. Application code must not learn which implementation it was given.
- Nine generation engines are discovered by assembly scanning. An engine is only registered if it carries `[DocumentEngine]`; adding a project without the attribute silently produces no engine.

## Working rules
- Keep changes small and task-focused.
- Do not introduce new dependencies unless they are necessary for the requested task.
- **MassTransit is pinned to 8.5.10.** Version 9 requires a paid licence, so upgrading is a commercial decision, not a routine bump.
- Update documentation when behavior, setup steps, or project structure changes. `README.md` and `docs/architecture.md` have Russian counterparts (`README_ru.md`, `docs/architecture_ru.md`); a factual change to one belongs in both.
- Verify against the code rather than the prose. Documentation in this repository has drifted before — several claims were corrected only after a live run contradicted them.

## File and code conventions
- Tests use xUnit with FluentAssertions and **hand-rolled fakes; there is no mocking library**. Follow that pattern rather than adding one.
- Container-backed tests are tagged `Category=RequiresDocker` and must use `[SkippableFact]`, not `[Fact]` with `Skip.If` — the latter reports a skip as a hard failure when Docker is unavailable.
- Inject `TimeProvider` rather than calling `DateTimeOffset.UtcNow` where TTL or expiry is involved, so tests stay deterministic.
- Validate configuration at startup, not on first use. A malformed setting should fail the host with a clear message.
- Avoid placeholder code that is not directly useful for the requested task.
- Keep naming consistent with `GenerateDocService`.

## Copilot skill usage
- Use the repository skills in `.github/skills/` when working on bootstrapping, documentation, service design, document generation, API surface, infrastructure, resilience, security, testing, async C#, or architecture review.
- C# conventions are in `.github/instructions/csharp.instructions.md`.

## Design and Implementation Guidelines
- Prefer solution design and implementation in C#/.NET using Clean Architecture and Modular Monolith principles.
- Adhere to SOLID principles, Clean Code, DRY, KISS, and YAGNI methodologies.
- Utilize design patterns such as Strategy, Factory, Mediator, and Plugin for a high-load document generation service.
- Retry is a decision, not a default. An engine calling a non-idempotent third party must throw `NonRetryableGenerationException`; see `docs/architecture.md` §3.2.

<!-- mermaid-ai-skills:start -->
## Mermaid Diagrams

When the user asks to create, edit, or visualize a diagram, follow the
instructions in `.github/instructions/mermaid.instructions.md`.
<!-- mermaid-ai-skills:end -->
