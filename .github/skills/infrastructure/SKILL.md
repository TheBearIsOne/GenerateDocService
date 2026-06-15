---
name: Infrastructure
summary: Use this skill when configuring Docker, Redis, RabbitMQ, MinIO, or local development infrastructure for GenerateDocService.
---

# Infrastructure

Use this skill when:
- running or modifying docker-compose.yml;
- configuring Redis cache/deduplication providers;
- setting up RabbitMQ transport;
- configuring MinIO object storage;
- writing local bootstrap scripts or developer guides.

## Goals
- Reproducible local environment
- Parity between local and production infrastructure
- Explicit provider configuration (InMemory vs Redis/RabbitMQ/ObjectStorage)

## Recommended approach
1. Define all infrastructure in deploy/docker/docker-compose.yml
2. Use health checks for all dependencies (Redis, RabbitMQ, MinIO)
3. Expose provider choice via appsettings:
   - DocumentProcessing:Caching:Provider = InMemory | Redis
   - DocumentProcessing:Messaging:Transport = InMemory | RabbitMQ
   - DocumentProcessing:Storage:Provider = InMemory | ObjectStorage
4. Keep default to InMemory for zero-setup local development
5. Document bootstrap steps in a single developer guide

## Output expectations
- Single docker compose command for full stack
- Explicit provider configuration in appsettings
- Health check endpoints for all dependencies
- Developer bootstrap documentation
