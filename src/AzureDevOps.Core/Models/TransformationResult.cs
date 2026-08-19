namespace AzureDevOps.Core.Models;

public record TransformationResult(
    bool Success,
    int ExitCode,
    string Output,
    string? ErrorMessage,
    TimeSpan Duration
);
