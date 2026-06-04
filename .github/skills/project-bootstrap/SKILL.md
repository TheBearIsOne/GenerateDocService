---
name: Project bootstrap
summary: Use this skill when the repository is still being shaped and a task requires creating the initial project structure, scaffolding, or conventions.
---

# Project bootstrap

Use this skill when:
- the repository has little or no source code yet;
- a task requires choosing an initial folder layout;
- you need to scaffold the first service, library, or test project;
- the requested implementation does not specify a language or framework.

## Goals
- Keep the initial structure small and easy to evolve.
- Avoid over-engineering.
- Make setup and next steps obvious from the repository contents.

## Recommended approach
1. Inspect the repository before assuming a stack.
2. Prefer a minimal layout such as:
   - `src/` for application code
   - `tests/` for automated tests
   - `docs/` for supplementary documentation when needed
3. Choose names that align with `GenerateDocService`.
4. Add only the files required to complete the task.
5. If a major architectural choice is required and there is no evidence in the repo, keep the solution conservative and document the assumption.

## Output expectations
- Small, reviewable scaffolding.
- Clear naming.
- Documentation updates when setup steps or structure are introduced.
