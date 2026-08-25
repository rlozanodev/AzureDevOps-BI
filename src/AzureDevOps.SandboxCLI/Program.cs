using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.IngestionWorker.Services.AzureDevOps;
using AzureDevOps.SandboxCLI;

var services = new ServiceCollection();

// Configure Logging
services.AddLogging(builder => 
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Configure AzureDevOpsOptions hardcoded
services.Configure<AzureDevOpsOptions>(options =>
{
    options.BaseUrl = "http://edvwp-tfs19-ap/";
    options.Collection = "DefaultCollection"; // or "Dir TI"
    options.ApiVersion = "5.0-preview";
    options.Auth = new AzureDevOpsAuthOptions
    {
        UseDefaultCredentials = false,
        Domain = "OFICINAS",
        Username = "roberto_lozano",
        Password = "Aten@301421468"
    };
});

// Register HttpSnifferHandler
services.AddTransient<HttpSnifferHandler>();

// Register HttpClient for AzureDevOpsClient
services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>()
    .ConfigurePrimaryHttpMessageHandler((sp) =>
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AzureDevOpsOptions>>().CurrentValue;
        return NtlmHttpHandlerFactory.CreateHandler(options.Auth);
    })
    .AddHttpMessageHandler<HttpSnifferHandler>();

var serviceProvider = services.BuildServiceProvider();

Console.WriteLine("Starting SandboxCLI to test NTLM Authentication...");

try
{
    var client = serviceProvider.GetRequiredService<IAzureDevOpsClient>();
    var options = serviceProvider.GetRequiredService<IOptionsMonitor<AzureDevOpsOptions>>().CurrentValue;
    
    Console.WriteLine($"Calling GetProjectsAsync on {options.BaseUrl}{options.Collection}...");
    var projects = await client.GetProjectsAsync(options.Collection);
    
    Console.WriteLine($"\nSuccess! Found {projects.Count} projects.");
    foreach (var p in projects)
    {
        Console.WriteLine($"- {p.Name} ({p.Id})");
    }
}
catch (HttpRequestException httpEx)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nHttpRequestException caught!");
    Console.WriteLine($"StatusCode: {httpEx.StatusCode}");
    Console.WriteLine($"Message: {httpEx.Message}");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nUnexpected Exception caught!");
    Console.WriteLine(ex.ToString());
    Console.ResetColor();
}

Console.WriteLine("\nPress ENTER to exit...");
Console.ReadLine();
