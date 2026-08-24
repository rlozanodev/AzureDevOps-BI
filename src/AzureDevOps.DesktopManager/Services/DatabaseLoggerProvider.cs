using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AzureDevOps.DesktopManager.Services;

public class DatabaseLoggerProvider : ILoggerProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, DatabaseLogger> _loggers = new();

    public DatabaseLoggerProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new DatabaseLogger(name, _serviceProvider));
    }

    public void Dispose() => _loggers.Clear();
}

public class DatabaseLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IServiceProvider _serviceProvider;

    public DatabaseLogger(string categoryName, IServiceProvider serviceProvider)
    {
        _categoryName = categoryName;
        _serviceProvider = serviceProvider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        
        var message = formatter(state, exception);
        var log = new SystemLogEntity
        {
            LogLevel = logLevel.ToString(),
            SourceContext = _categoryName,
            Message = message,
            Exception = exception?.ToString() ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Fire and forget
        _ = Task.Run(async () => 
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ILogRepository>();
                await repo.InsertLogAsync(log);
            }
            catch { }
        });
    }
}
