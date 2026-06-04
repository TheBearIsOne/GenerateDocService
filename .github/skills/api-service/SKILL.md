---
name: API service
summary: Use this skill when creating or extending HTTP API endpoints, request/response contracts, or service integration points for GenerateDocService.
---

# API service

Use this skill when:
- adding HTTP endpoints;
- defining request and response models;
- wiring document generation into an API surface;
- shaping controller, route, or handler behavior.

## Goals
- Keep the API surface small and explicit.
- Validate inputs close to the boundary.
- Keep endpoint logic thin by delegating work to focused services.

## Recommended approach
1. Define request and response contracts clearly.
2. Keep endpoint code responsible for validation, orchestration, and status mapping.
3. Move document-generation logic into dedicated service classes or functions.
4. Return errors consistently and avoid leaking internal details.
5. Document any new endpoint behavior in the README or docs when applicable.

## Output expectations
- Thin API layer.
- Clear request/response models.
- Separation between transport and generation logic.
