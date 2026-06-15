# Performance Baseline

## Overview

This document describes the standard benchmark matrix for comparing document generation performance across engines, routing modes, and cache states.

Benchmarks are run using the NBomber project at `tests/Performance/GenerateDocService.LoadTests.NBomber`.

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
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_warm async_scriban_explicit
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_cold async_scriban_explicit
```

### Scriban vs DotLiquid
```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_explicit async_dotliquid_explicit
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_dotliquid_explicit async_scriban_explicit
```

### PDF Baseline
```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_questpdf async_scriban_explicit
```

### Explicit vs Auto-Selection
```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_explicit async_scriban_auto
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
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- https://localhost:7001 sync_scriban_warm async_scriban_explicit .\artifacts\perf
```

Keep report folders as historical snapshots for regression comparison.

## Running Baselines

Recommended workflow:
1. Run one warm-cache baseline
2. Run one cold-cache baseline
3. Compare Scriban and DotLiquid using the same input/output shape
4. Keep report folders for historical regression tracking
