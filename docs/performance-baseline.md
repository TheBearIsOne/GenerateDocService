# Performance Baseline

## Overview

This document describes the standard benchmark matrix for comparing document generation performance across engines, routing modes, and cache states.

There are **two tiers**, and they answer different questions:

| Tier | Project | Measures | Needs |
|------|---------|----------|-------|
| End-to-end load | `tests/Performance/GenerateDocService.LoadTests.NBomber` | the whole HTTP pipeline, including routing, validation, and transport | a running API |
| In-process microbenchmark | `src/Clients/GenerateDocService.SampleClient` (`-- benchmark`) | a single component — an engine, a template parser, the fingerprint or base64 path | nothing but a Release build |

Use NBomber to find out whether the *service* is fast enough; use BenchmarkDotNet to find out *which component* is responsible. Neither substitutes for the other: NBomber's numbers include HTTP and serialization overhead that dwarfs most engine differences, while the microbenchmarks see none of it.

The rest of this document covers the NBomber tier; the microbenchmark tier is described in [In-process microbenchmarks](#in-process-microbenchmarks) below.

NBomber targets the API base URL given as its first argument. The examples use `http://localhost:5287`, the `dotnet run` address from `Properties/launchSettings.json`. Against the Docker stack the API is on `http://localhost:8081` **and requires a JWT**, which the profiles do not supply — run them against a locally started API with `DocumentProcessing:Authentication:Enabled=false`.

## Benchmark Matrix

### Sync Profiles

| Profile | Engine | Routing | Cache State | Purpose |
|---------|--------|---------|-------------|---------|
| `sync_scriban_explicit` | Scriban | Explicit by name | Warm | Baseline for explicit engine selection |
| `sync_scriban_auto` | Scriban | Auto-select | Warm | Overhead of auto-selection |
| `sync_scriban_warm` | Scriban | Explicit | Warm (pre-loaded) | Template cache hit scenario |
| `sync_scriban_cold` | Scriban | Explicit | Cold (no cache) | Template compilation cost |
| `sync_dotliquid_explicit` | DotLiquid | Explicit by name | Warm | Compare with Scriban |
| `sync_dotliquid_auto` | DotLiquid | Auto-select | Warm | Overhead of auto-selection |
| `sync_dotliquid_warm` | DotLiquid | Explicit | Warm | Cache hit scenario |
| `sync_dotliquid_cold` | DotLiquid | Explicit | Cold | Compilation cost |
| `sync_questpdf` | QuestPDF | Explicit by name | N/A | PDF generation baseline |

### Async Profiles

| Profile | Engine | Routing | Purpose |
|---------|--------|---------|---------|
| `async_scriban_explicit` | Scriban | Explicit by name | Baseline async |
| `async_scriban_auto` | Scriban | Auto-select | Overhead of auto-selection |
| `async_dotliquid_explicit` | DotLiquid | Explicit by name | Compare with Scriban |
| `async_dotliquid_auto` | DotLiquid | Auto-select | Overhead of auto-selection |

## Standard Comparison Workflows

### Warm vs Cold Cache (Scriban)
```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_warm async_scriban_explicit
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_cold async_scriban_explicit
```

### Scriban vs DotLiquid
```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_explicit async_dotliquid_explicit
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_dotliquid_explicit async_scriban_explicit
```

### PDF Baseline
```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_questpdf async_scriban_explicit
```

`sync_questpdf` measures the in-process PDF engine. The FOP adapter is an external HTTP dependency and does not yet have an NBomber profile; establish its own baseline only after the converter host, payload size, and network placement are fixed.

For manual correctness inspection, the unit suite saves outputs from every in-process engine under `artifacts/test-results/<engine>/`, including populated TXT, HTML, Markdown, XLSX, DOCX, and PDF artifacts. The XLSX and DOCX template checks inspect the resulting Open XML to verify data binding. Set `FOP_CONVERTER_BASE_URL` and run the FOP integration test to render a populated Scriban XSL-FO/XML template and add `artifacts/test-results/fop/fop-template-generated.pdf`. These files are test artifacts, not performance reports, and are ignored by Git.

### Explicit vs Auto-Selection
```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_explicit async_scriban_auto
```

## Metrics

Each benchmark reports:
- **RPS** (requests per second)
- **Latency percentiles**: p50, p90, p95, p99
- **Throughput** (bytes/sec for generated documents)
- **Error rate**

Custom metrics emitted by the API:
- `document_generation_sync_requests_total`
- `document_generation_async_requests_total`
- `document_generation_completed_total`
- `document_generation_failed_total`
- `document_generation_duration_ms`

All metrics include tags: `engine`, `output_format`.

## Report Storage

NBomber reports are written to:
```
artifacts/perf/<timestamp>-<sync-profile>-<async-profile>/
```

Override with a 4th CLI argument:
```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_warm async_scriban_explicit .\artifacts\perf
```

Keep report folders as historical snapshots for regression comparison.

## In-process microbenchmarks

BenchmarkDotNet 0.15.8 is hosted inside the sample client, so component-level numbers need no API, no Docker and no FOP converter. **Release is mandatory** — BenchmarkDotNet refuses a Debug build — and the benchmark classes must stay unsealed, which BenchmarkDotNet also enforces.

```powershell
# Everything (takes a while)
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark

# One class, or one category
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --filter *TemplateParserCacheBenchmarks*
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --anyCategories text

# Smoke-check the harness without waiting for statistics
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --job Dry --filter *Scriban*
```

`benchmark` (or `bench`) is also a CLI mode and the `b` command in the interactive menu; any arguments after it pass straight through to BenchmarkDotNet. Under a Debug build the client prints the Release command instead of failing obscurely.

Three classes live in `src/Clients/GenerateDocService.SampleClient/Benchmarks/InProcessBenchmarks.cs`, all annotated `[MemoryDiagnoser]`:

| Class | Categories | Measures |
|-------|-----------|----------|
| `EngineGenerationBenchmarks` | `text`, `office`, `pdf` | scriban, dotliquid, questpdf, the miniexcel table and template paths, and miniword |
| `TemplateParserCacheBenchmarks` | `scriban`, `dotliquid` | template compilation on a cache miss against a warm `InMemoryCompiledTemplateCache` hit |
| `RequestPipelineBenchmarks` | `fingerprint`, `transport` | request fingerprinting at 1 KB and 1 MB, and base64 encode/decode of a 1 MB payload |

Every payload, template and request is constructed in `[GlobalSetup]`. Allocating inside a measured method attributes the fixture's cost to the component under test, which is the easiest way to produce a number that looks meaningful and is not.

Reports are written to `BenchmarkDotNet.Artifacts/results/`, which is gitignored.

**Coverage gaps.** `GenerateDocumentRequestValidator` is not benchmarked: it lives in the API host, and referencing that project from the sample client would pull the whole web host into a demo executable. Covering it needs either a shared validation library or a benchmark project outside `src/Clients`. The `fop` and `api` engines are not benchmarked either, since both are network-bound and a microbenchmark would measure the mock rather than the engine.

No baseline is committed yet, so results currently have nothing to regress against — see `docs/improvement-plan.md` §11 item 28.

## Running Baselines

Recommended workflow:
1. Run one warm-cache baseline
2. Run one cold-cache baseline
3. Compare Scriban and DotLiquid using the same input/output shape
4. Keep report folders for historical regression tracking
5. When a change touches an engine, parser, or the request pipeline, run the matching BenchmarkDotNet class before and after — it isolates the component far better than an end-to-end NBomber run can
