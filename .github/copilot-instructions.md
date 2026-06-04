# Copilot instructions for GenerateDocService

## Project context
- This repository is the starting point for `GenerateDocService`.
- The repository is currently minimal, so inspect the workspace before assuming a stack, framework, or folder layout.
- Prefer incremental changes that keep the project easy to bootstrap and review.

## Working rules
- Keep changes small and task-focused.
- Do not introduce new dependencies unless they are necessary for the requested task.
- When scaffolding new code, keep naming consistent with `GenerateDocService`.
- If the stack is not yet defined, propose the smallest viable structure instead of generating a large template.
- Update documentation when behavior, setup steps, or project structure changes.

## File and code conventions
- Prefer clear folder names such as `src`, `tests`, and `docs` when creating new structure.
- Keep configuration files minimal.
- Avoid placeholder code that is not directly useful for the requested task.
- Preserve existing language and formatting conventions once real source files appear.

## Copilot skill usage
- Use the repository skills in `.github/skills/` when working on bootstrapping, documentation, or service design tasks.

## Design and Implementation Guidelines
- Prefer solution design and implementation in C#/.NET using Clean Architecture and Modular Monolith principles.
- Adhere to SOLID principles, Clean Code, DRY, KISS, and YAGNI methodologies.
- Utilize design patterns such as Strategy, Factory, Mediator, and Plugin for a high-load document generation service.
