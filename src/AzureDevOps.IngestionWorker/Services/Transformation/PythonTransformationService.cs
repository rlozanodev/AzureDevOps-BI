using System.Diagnostics;
using System.Text;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOps.IngestionWorker.Services.Transformation;

public class PythonTransformationService : IPythonTransformationService
{
    private readonly TransformationOptions _options;
    private readonly ILogger<PythonTransformationService> _logger;

    public PythonTransformationService(
        IOptions<TransformationOptions> options,
        ILogger<PythonTransformationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TransformationResult> RunTransformationAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Python OLAP transformation is disabled via configuration.");
            return new TransformationResult(true, 0, "Transformation skipped (disabled)", null, TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        var (scriptFullPath, workingDir) = ResolvePaths(_options.ScriptPath);

        _logger.LogInformation("Starting DuckDB OLAP transformation script: {ScriptPath} (WorkingDir: {WorkingDir}) using {Executable}...",
            scriptFullPath, workingDir, _options.PythonExecutable);

        string exe = _options.PythonExecutable;
        string args;

        if (exe.Equals("uv", StringComparison.OrdinalIgnoreCase))
        {
            args = $"run \"{scriptFullPath}\"";
        }
        else
        {
            args = $"\"{scriptFullPath}\"";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                _logger.LogInformation("[DuckDB/Python] {Line}", e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                _logger.LogWarning("[DuckDB/Python stderr] {Line}", e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            await process.WaitForExitAsync(cts.Token);

            stopwatch.Stop();
            var success = process.ExitCode == 0;
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (success)
            {
                _logger.LogInformation("DuckDB transformation completed successfully in {Elapsed:N2}s (Exit code: 0).", stopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                _logger.LogError("DuckDB transformation failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
            }

            return new TransformationResult(
                success,
                process.ExitCode,
                output,
                string.IsNullOrWhiteSpace(error) ? null : error,
                stopwatch.Elapsed
            );
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            try { if (!process.HasExited) process.Kill(true); } catch { /* ignore */ }
            _logger.LogError("DuckDB transformation timed out after {Timeout}s.", _options.TimeoutSeconds);
            return new TransformationResult(false, -1, outputBuilder.ToString(), "Process execution timed out or was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            try { if (!process.HasExited) process.Kill(true); } catch { /* ignore */ }
            _logger.LogError(ex, "Unexpected error executing Python transformation.");
            return new TransformationResult(false, -1, outputBuilder.ToString(), ex.Message, stopwatch.Elapsed);
        }
    }

    public async Task<TransformationResult> ExportProjectToParquetAsync(string projectName, string outputFilePath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var scriptPath = "analytics_engine/export_parquet.py";
        var (scriptFullPath, workingDir) = ResolvePaths(scriptPath);

        _logger.LogInformation("Starting Parquet Export: {ScriptPath} (Project: {ProjectName}, Output: {OutputPath})",
            scriptFullPath, projectName, outputFilePath);

        string exe = _options.PythonExecutable;
        // Escape arguments properly for shell
        string args = exe.Equals("uv", StringComparison.OrdinalIgnoreCase)
            ? $"run \"{scriptFullPath}\" \"{projectName}\" \"{outputFilePath}\""
            : $"\"{scriptFullPath}\" \"{projectName}\" \"{outputFilePath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(120)); // Generous timeout for export

            await process.WaitForExitAsync(cts.Token);

            stopwatch.Stop();
            var success = process.ExitCode == 0;
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (success)
                _logger.LogInformation("Export to Parquet completed successfully in {Elapsed:N2}s.", stopwatch.Elapsed.TotalSeconds);
            else
                _logger.LogError("Export to Parquet failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);

            return new TransformationResult(success, process.ExitCode, output, string.IsNullOrWhiteSpace(error) ? null : error, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            try { if (!process.HasExited) process.Kill(true); } catch { /* ignore */ }
            return new TransformationResult(false, -1, outputBuilder.ToString(), "Export timed out or was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            try { if (!process.HasExited) process.Kill(true); } catch { /* ignore */ }
            return new TransformationResult(false, -1, outputBuilder.ToString(), ex.Message, stopwatch.Elapsed);
        }
    }

    private static (string scriptFullPath, string workingDirectory) ResolvePaths(string configuredScriptPath)
    {
        if (Path.IsPathRooted(configuredScriptPath) && File.Exists(configuredScriptPath))
        {
            return (configuredScriptPath, Path.GetDirectoryName(configuredScriptPath)!);
        }

        // Search upward from current directory to locate workspace root containing the script
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null)
        {
            var directMatch = Path.Combine(currentDir.FullName, configuredScriptPath);
            if (File.Exists(directMatch))
            {
                return (directMatch, currentDir.FullName);
            }

            var inEngine = Path.Combine(currentDir.FullName, "analytics_engine", "transform_analytics.py");
            if (File.Exists(inEngine))
            {
                return (inEngine, currentDir.FullName);
            }

            currentDir = currentDir.Parent;
        }

        var fallback = Path.GetFullPath(configuredScriptPath);
        return (fallback, Directory.GetCurrentDirectory());
    }
}
