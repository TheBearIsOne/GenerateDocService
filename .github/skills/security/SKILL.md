---
name: Security
summary: Use this skill when implementing authentication, authorization, JWT, role-based access, or security hardening for GenerateDocService.
---

# Security

Use this skill when:
- adding authentication (JWT bearer, API keys, OAuth);
- implementing role-based or policy-based authorization;
- protecting generation, task, or download endpoints;
- reviewing security configuration or secrets handling.

## Goals
- Protect sensitive document data in transit and at rest
- Enforce least-privilege access per endpoint
- Keep auth configuration explicit and testable

## Recommended approach
1. Use ASP.NET Core JWT bearer authentication for service-to-service auth
2. Define explicit roles/scopes: submit, read-status, download, admin
3. Protect POST /documents/* with submit role
4. Protect GET /tasks/* and GET /tasks/{id}/download with read-status/download roles
5. Keep JWT validation explicit: issuer, audience, signing key, clock skew
6. Never log tokens, secrets, or sensitive payload content

## Output expectations
- Explicit auth middleware configuration
- Policy-based authorization on controllers/endpoints
- Clear error responses (401 vs 403)
- No secrets in code or config files — use environment variables or secret managers
