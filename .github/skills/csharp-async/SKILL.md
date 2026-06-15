---
name: C# async
summary: Use this skill when writing, reviewing, or refactoring async C# code in GenerateDocService. Covers naming, return types, exception handling, performance, and common pitfalls.
---

# C# async

Use this skill when:
- writing or reviewing asynchronous C# methods;
- refactoring sync code to async;
- troubleshooting async deadlocks or performance issues;
- designing TAP (Task-based Asynchronous Pattern) APIs.

## Goals
- Prevent deadlocks and resource starvation
- Keep async code consistent and readable
- Minimize allocations in high-throughput paths

## Recommended approach
1. Use `Async` suffix for all async methods
2. Return `Task<T>` or `Task`, never `void` (except event handlers)
3. Use `ConfigureAwait(false)` in library code
4. Propagate `CancellationToken` through the call chain
5. Prefer `Task.WhenAll` for independent parallel operations

## Common pitfalls to flag
- `.Wait()`, `.Result`, `.GetAwaiter().GetResult()` — blocks thread pool
- `async void` — unhandled exceptions crash the process
- Missing `await` on `Task`-returning methods — fire-and-forget bugs
- Unnecessary `async/await` — pass `Task` directly when not using result

## Performance
- Consider `ValueTask<T>` for hot paths with frequent synchronous completion
- Use `IAsyncEnumerable<T>` for streaming large result sets
- Avoid allocating lambdas/delegates in tight async loops
