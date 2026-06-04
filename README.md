# GenerateDocService

## Overview

Initial Clean Architecture scaffold for a high-load document generation and transformation service on .NET.

Current first iteration includes:

- solution and project structure for Domain, Application, Infrastructure, Presentation, Host, and Client;
- engine abstractions;
- in-memory engine registry and task repository;
- demo `fake` generation engine;
- sync and async HTTP endpoints;
- sample .NET client;
- first unit test.

## Projects

- `src/Engines/GenerateDocService.Engine.Abstractions`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Domain`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation`
- `src/Host/GenerateDocService.Api`
- `src/Clients/GenerateDocService.SampleClient`
- `tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests`

## API

Current endpoints:

- `GET /health`
- `POST /api/v1/documents/sync`
- `POST /api/v1/documents/async`
- `GET /api/v1/tasks/{taskId}`

Example request body:

```json
{
  "requestId": "optional-request-id",
  "engine": "fake",
  "inputFormat": "json",
  "outputFormat": "txt",
  "templateFormat": null,
  "payload": "{\"document\":\"hello\"}",
  "template": null,
  "metadata": {
    "client": "sample"
  }
}
```

## Run

Start API:

```powershell
dotnet run --project src/Host/GenerateDocService.Api
```

Run sample client:

```powershell
dotnet run --project src/Clients/GenerateDocService.SampleClient -- https://localhost:7001
```

## Validation

Build:

```powershell
dotnet build .\GenerateDocService.slnx
```

Test:

```powershell
dotnet test tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests/GenerateDocService.DocumentProcessing.Application.Tests.csproj
```

## Copilot skills

Repository guidance for Copilot is stored in:

- `.github/copilot-instructions.md`
- `.github/skills/project-bootstrap/SKILL.md`
- `.github/skills/documentation-work/SKILL.md`
- `.github/skills/service-design/SKILL.md`
- `.github/skills/document-generation/SKILL.md`
- `.github/skills/testing/SKILL.md`
- `.github/skills/api-service/SKILL.md`
- `.github/skills/senior-architect/SKILL.md`
- `.github/skills/csharp-developer/SKILL.md`
