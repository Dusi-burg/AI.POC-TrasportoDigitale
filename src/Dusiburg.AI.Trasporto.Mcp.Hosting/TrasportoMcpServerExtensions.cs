using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dusiburg.AI.Trasporto.Mcp.Hosting;

/// <summary>
/// Composizione comune dei server MCP del POC: trasporto HTTP stateless su <c>/mcp</c>. API key di ruolo, filtro sulle chiamate
/// ai tool ed errori strutturati si aggiungono con le fasi 4 e 5, prima del primo tool.
/// </summary>
public static class TrasportoMcpServerExtensions
{
    public const string EndpointPath = "/mcp";

    /// <summary>Registra il server MCP; i tool si aggiungono sul builder restituito (<c>WithTools&lt;T&gt;()</c>).</summary>
    public static IMcpServerBuilder AddTrasportoMcpServer(this IHostApplicationBuilder builder) =>
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true);

    public static IEndpointConventionBuilder MapTrasportoMcp(this WebApplication app) => app.MapMcp(EndpointPath);
}
