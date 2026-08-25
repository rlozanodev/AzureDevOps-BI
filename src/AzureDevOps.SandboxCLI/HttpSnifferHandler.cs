using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AzureDevOps.SandboxCLI;

public class HttpSnifferHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================= HTTP REQUEST =================");
        Console.WriteLine($"{request.Method} {request.RequestUri}");
        Console.WriteLine("Headers:");
        foreach (var header in request.Headers)
        {
            Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        }

        if (request.Content != null)
        {
            Console.WriteLine("Content Headers:");
            foreach (var header in request.Content.Headers)
            {
                Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }

            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrEmpty(body))
            {
                Console.WriteLine("Body:");
                Console.WriteLine(body);
            }
        }
        Console.WriteLine("================================================");
        Console.ResetColor();

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("================ HTTP EXCEPTION ================");
            Console.WriteLine(ex.ToString());
            Console.WriteLine("================================================");
            Console.ResetColor();
            throw;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("================= HTTP RESPONSE ================");
        Console.WriteLine($"StatusCode: {(int)response.StatusCode} {response.ReasonPhrase}");
        Console.WriteLine("Headers:");
        foreach (var header in response.Headers)
        {
            Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        }
        
        if (response.Content != null)
        {
            Console.WriteLine("Content Headers:");
            foreach (var header in response.Content.Headers)
            {
                Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrEmpty(body))
            {
                Console.WriteLine("Body:");
                Console.WriteLine(body);
            }
        }
        Console.WriteLine("================================================");
        Console.ResetColor();

        return response;
    }
}
