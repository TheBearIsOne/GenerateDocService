---
description: "Use when creating or modifying C# code in GenerateDocService. Covers .NET 10 conventions, nullability, asynchronous APIs, and maintainable service code."
name: "GenerateDocService C# Conventions"
applyTo: "**/*.cs"
---
# GenerateDocService C# Conventions

- Target .NET 10 and preserve nullable reference type correctness; use nullable annotations instead of suppressions.
- Use file-scoped namespaces and implicit global usings. Add explicit `using` directives only when they improve clarity.
- Accept and propagate a `CancellationToken` for asynchronous and potentially long-running operations. Name asynchronous methods with the `Async` suffix.
- Prefer simple, immutable data shapes: `sealed` concrete types, primary constructors, and expression-bodied members where they keep code clear.
- Validate public method inputs at the boundary and throw specific framework exceptions for invalid states or unsupported operations.
- Keep dependencies behind the existing abstractions. Do not introduce packages or cross-module references unless the requested behavior requires them.
- Use `StringComparison.OrdinalIgnoreCase` for case-insensitive format and identifier comparisons.