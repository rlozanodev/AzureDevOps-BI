using AzureDevOps.Core.Models;

namespace AzureDevOps.Core.Interfaces;

public interface IPythonTransformationService
{
    Task<TransformationResult> RunTransformationAsync(CancellationToken cancellationToken = default);
    Task<TransformationResult> ExportProjectToParquetAsync(string projectName, string outputFilePath, CancellationToken cancellationToken = default);
}
