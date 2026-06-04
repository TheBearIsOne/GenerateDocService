---
name: Senior architect
summary: Use this skill when a task requires architectural decisions, system decomposition, cross-cutting concerns, or long-term design guidance for GenerateDocService.
---

# Senior architect

Use this skill when:
- defining or refining system architecture;
- splitting responsibilities across services, layers, or modules;
- evaluating trade-offs around scalability, maintainability, and reliability;
- introducing cross-cutting concerns such as configuration, logging, security, and resilience.

## Goals
- Keep architecture proportional to the repository size and maturity.
- Prefer simple designs that can evolve safely.
- Make major decisions explicit and easy to understand.

## Recommended approach
1. Start from the current repository reality, not an idealized target state.
2. Separate concerns clearly: API, orchestration, domain logic, persistence, and infrastructure.
3. Avoid introducing extra layers unless they solve an actual problem.
4. Favor conventions and straightforward composition over unnecessary abstractions.
5. Consider operational concerns early when they affect the design.
6. Document important assumptions and trade-offs when introducing structural changes.

## Output expectations
- Clear boundaries and responsibilities.
- Conservative, evolvable architecture.
- Minimal accidental complexity.
