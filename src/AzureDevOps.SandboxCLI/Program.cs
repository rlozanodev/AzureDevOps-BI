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
    options.Collection = "Dir TI"; // or "Dir TI"
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

services.AddSingleton<IDynamicConfigProvider>(sp => 
{
    var options = sp.GetRequiredService<IOptions<AzureDevOpsOptions>>();
    return new SandboxConfigProvider(options);
});

// Register HttpClient for AzureDevOpsClient
services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>()
    .ConfigurePrimaryHttpMessageHandler((sp) =>
    {
        var configProvider = sp.GetRequiredService<IDynamicConfigProvider>();
        return NtlmHttpHandlerFactory.CreateHandler(configProvider);
    })
    .AddHttpMessageHandler<HttpSnifferHandler>();

var serviceProvider = services.BuildServiceProvider();

Console.WriteLine("Starting SandboxCLI to test NTLM Authentication...");

try
{
    var client = serviceProvider.GetRequiredService<IAzureDevOpsClient>();
    var options = serviceProvider.GetRequiredService<IOptionsMonitor<AzureDevOpsOptions>>().CurrentValue;
    
    Console.WriteLine($"\n🔍 Explorando topología de Azure DevOps Server ({options.BaseUrl})...");
    
    var collections = await client.GetCollectionsAsync();
    
    if (collections == null || collections.Count == 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️ No se encontró ninguna colección.");
        Console.ResetColor();
        return;
    }

    Console.WriteLine($"\n📦 Encontradas {collections.Count} Colección(es):");

    foreach (var col in collections)
    {
        Console.WriteLine($"\n=======================================================");
        Console.WriteLine($"🏢 Colección: {col.Name}");
        Console.WriteLine($"   ID: {col.Id}");
        Console.WriteLine($"   Estado: {col.State}");
        Console.WriteLine($"=======================================================");

        try
        {
            var projects = await client.GetProjectsAsync(col.Name);
            Console.WriteLine($"\n   📑 Proyectos ({projects.Count}):");
            
            foreach (var p in projects)
            {
                Console.WriteLine($"      - {p.Name} [ID: {p.Id}, Estado: {p.State}]");
            }
        }
        catch (HttpRequestException httpEx)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n   ❌ Error al obtener proyectos de '{col.Name}':");
            Console.WriteLine($"      StatusCode: {httpEx.StatusCode}");
            Console.WriteLine($"      Mensaje: {httpEx.Message}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n   ❌ Excepción inesperada en '{col.Name}': {ex.Message}");
            Console.ResetColor();
        }
    }

    Console.WriteLine("\n\n=======================================================");
    Console.WriteLine("🚀 INICIANDO PRUEBA DE EXTRACCIÓN DE WORK ITEMS");
    Console.WriteLine("=======================================================");
    
    var testCollection = "Dir TI";
    var testProject = "Pruebas Automatizadas";
    
    Console.WriteLine($"\n🔍 Consultando IDs de Work Items para la colección '{testCollection}' y proyecto '{testProject}'...");
    
    var workItemIds = await client.QueryWorkItemIdsAsync(testCollection, testProject, null);
    
    if (workItemIds == null || workItemIds.Count == 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️ No se encontraron Work Items para este proyecto.");
        Console.ResetColor();
    }
    else
    {
        Console.WriteLine($"\n✅ Encontrados {workItemIds.Count} Work Item(s) en total por WIQL.");
        
        var batchIds = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Take(workItemIds, 5));
        Console.WriteLine($"\n📦 Descargando detalles (Batching) para los primeros {batchIds.Count} IDs: {string.Join(", ", batchIds)}...");
        
        var workItems = await client.GetWorkItemsBatchAsync(testCollection, batchIds);
        
        Console.WriteLine($"\n📋 Resultados del Batch ({workItems.Count} elementos devueltos):");
        foreach (var wi in workItems)
        {
            var wiType = wi.GetFieldValue<string>("System.WorkItemType") ?? "Desconocido";
            var state = wi.GetFieldValue<string>("System.State") ?? "Desconocido";
            var title = wi.GetFieldValue<string>("System.Title") ?? "Sin Título";
            
            Console.WriteLine($"   - [ID: {wi.Id}] [{wiType}] {title} (Estado: {state})");
        }
    }
}
catch (HttpRequestException httpEx)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n❌ HttpRequestException general atrapada!");
    Console.WriteLine($"StatusCode: {httpEx.StatusCode}");
    Console.WriteLine($"Message: {httpEx.Message}");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n❌ Excepción inesperada general!");
    Console.WriteLine(ex.ToString());
    Console.ResetColor();
}

Console.WriteLine("\nPress ENTER to exit...");
Console.ReadLine();

class SandboxConfigProvider : AzureDevOps.Core.Interfaces.IDynamicConfigProvider
{
    public AzureDevOps.Core.Configuration.AzureDevOpsOptions Current { get; }
    public SandboxConfigProvider(Microsoft.Extensions.Options.IOptions<AzureDevOps.Core.Configuration.AzureDevOpsOptions> options) => Current = options.Value;
    public System.Threading.Tasks.Task<AzureDevOps.Core.Configuration.AzureDevOpsOptions> GetConfigAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Current);
    public System.Threading.Tasks.Task UpdateConfigAsync(AzureDevOps.Core.Configuration.AzureDevOpsOptions newConfig, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
}
