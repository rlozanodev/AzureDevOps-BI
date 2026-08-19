using AzureDevOps.Core.Models;

namespace AzureDevOps.Core.Interfaces;

public interface IPythonTransformationService
{
    Task<TransformationResult> RunTransformationAsync(CancellationToken cancellationToken = default);
}
