FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Host/GenerateDocService.Worker/GenerateDocService.Worker.csproj", "src/Host/GenerateDocService.Worker/"]
COPY ["src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure/GenerateDocService.DocumentProcessing.Infrastructure.csproj", "src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure/"]
COPY ["src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application/GenerateDocService.DocumentProcessing.Application.csproj", "src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application/"]
COPY ["src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Domain/GenerateDocService.DocumentProcessing.Domain.csproj", "src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Domain/"]
COPY ["src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation/GenerateDocService.DocumentProcessing.Presentation.csproj", "src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation/"]
COPY ["src/Engines/GenerateDocService.Engine.Abstractions/GenerateDocService.Engine.Abstractions.csproj", "src/Engines/GenerateDocService.Engine.Abstractions/"]
COPY ["src/Engines/GenerateDocService.Engine.Scriban/GenerateDocService.Engine.Scriban.csproj", "src/Engines/GenerateDocService.Engine.Scriban/"]
COPY ["src/Engines/GenerateDocService.Engine.DotLiquid/GenerateDocService.Engine.DotLiquid.csproj", "src/Engines/GenerateDocService.Engine.DotLiquid/"]
COPY ["src/Engines/GenerateDocService.Engine.QuestPdf/GenerateDocService.Engine.QuestPdf.csproj", "src/Engines/GenerateDocService.Engine.QuestPdf/"]
RUN dotnet restore "src/Host/GenerateDocService.Worker/GenerateDocService.Worker.csproj"
COPY . .
WORKDIR "/src/src/Host/GenerateDocService.Worker"
RUN dotnet build "GenerateDocService.Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GenerateDocService.Worker.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GenerateDocService.Worker.dll"]
