---
name: Testing
summary: Use this skill when adding or updating automated tests, validating behavior, or introducing a minimal test structure for GenerateDocService.
---

# Testing

Use this skill when:
- creating the first automated tests in the repository;
- updating tests after behavior changes;
- validating document-generation or service behavior;
- choosing a minimal testing approach for a new component.

## Goals
- Test behavior, not implementation details.
- Keep tests small, readable, and fast.
- Add only the minimum test structure needed for the task.

## Recommended approach
1. Match the existing test stack if one already exists.
2. If no test stack exists, introduce the smallest viable option only when necessary.
3. Prefer targeted unit tests for transformation and generation logic.
4. Use descriptive test names based on expected behavior.
5. Avoid broad integration scaffolding unless the task requires it.

## Output expectations
- Narrow, task-focused tests.
- Clear setup and assertions.
- Minimal additional complexity in the repository.
