---
name: Document generation
summary: Use this skill when implementing document creation, formatting, export pipelines, or template-based generation in GenerateDocService.
---

# Document generation

Use this skill when:
- generating documents from input data;
- adding export formats such as text, markdown, HTML, or PDF-oriented flows;
- introducing templates, mapping, or rendering steps;
- designing the core document-generation pipeline.

## Goals
- Keep generation deterministic and easy to test.
- Separate input mapping, rendering, and output writing.
- Prefer simple models that clearly represent document content.

## Recommended approach
1. Define the document input contract clearly.
2. Transform input data into an intermediate model when it improves clarity.
3. Keep formatting logic separate from transport and storage concerns.
4. Prefer pure functions for rendering where possible.
5. Add small examples or tests for expected output when infrastructure exists.

## Output expectations
- Focused generation logic.
- Clear boundaries between mapping, rendering, and persistence.
- Minimal assumptions about output format unless explicitly requested.
