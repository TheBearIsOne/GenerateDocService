---
name: Resilience
summary: Use this skill when adding retry, timeout, circuit-breaker, or resilience policies for external dependencies (Redis, RabbitMQ, MinIO, HTTP calls).
---

# Resilience

Use this skill when:
- configuring resilience policies for Redis, RabbitMQ, MinIO;
- adding retry, timeout, or circuit-breaker behavior;
- designing fallback strategies for dependency failures;
- reviewing error handling for external service calls.

## Goals
- Graceful degradation when dependencies fail
- Controlled failure propagation under load
- Observable resilience behavior (metrics, logs)

## Recommended approach
1. Use Polly or Microsoft.Extensions.Resilience for policy definitions
2. Configure per-dependency policies:
   - Redis: retry with exponential backoff + circuit-breaker
   - RabbitMQ: retry with dead-letter fallback
   - MinIO/object storage: retry + timeout + fallback to in-memory
3. Make policy configuration explicit via appsettings
4. Log policy triggers (retry, circuit-open) for observability
5. Never swallow errors — surface them to the caller or monitoring

## Output expectations
- Named resilience policies per dependency
- Configuration-driven policy parameters (retries, delays, thresholds)
- Clear fallback behavior when circuit is open
- Metrics or logs for policy activation events
