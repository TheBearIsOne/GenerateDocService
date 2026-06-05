namespace GenerateDocService.DocumentProcessing.Application.Models;

public sealed record GenerationRequestReservation(
    string TaskId,
    bool IsOwner);
