---
name: Service design
summary: Use this skill when implementing or shaping service-level functionality for GenerateDocService, especially for endpoints, background processing, and document-generation flows.
---

# Service design

Use this skill when:
- creating the first service implementation;
- adding endpoints or handlers;
- introducing background jobs or processing pipelines;
- implementing document-generation logic.

## Goals
- Keep service boundaries simple.
- Separate orchestration from transformation logic.
- Favor testable units over large multi-purpose classes.

## Recommended approach
1. Identify the input, processing steps, and output format.
2. Keep transport concerns separate from core generation logic.
3. Prefer interfaces only when they help testing or abstraction.
4. Avoid baking storage, transport, and rendering logic into one class.
5. Add tests for document-generation behavior when test infrastructure exists.

## Output expectations
- Focused components.
- Minimal coupling.
- Names that reflect document-generation intent clearly.
