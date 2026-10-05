using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dusiburg.AI.Trasporto.Mcp.Hosting;

/// <summary>
/// Composizione comune dei server MCP del POC: trasporto HTTP stateless su <c>/mcp</c>, raggiungibile solo con la chiave di un
/// ruolo. Il filtro sui tool per ruolo e gli errori strutturati dei tool si aggiungono con la Fase 5.
/// </summary>
public static class TrasportoMcpServerExtensions
{
    public const string EndpointPath = "/mcp";

    private static readonly object ActorItemKey = new();

    /// <summary>Registra il server MCP; i tool si aggiungono sul builder restituito (<c>WithTools&lt;T&gt;()</c>).</summary>
    public static IMcpServerBuilder AddTrasportoMcpServer(this IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton<ActorResolver>();

        return builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true);
    }

    /// <summary>
    /// Mappa l'endpoint MCP dietro la chiave di ruolo (<c>X-Api-Key</c>): senza una chiave riconosciuta la richiesta si ferma
    /// qui, con lo stesso involucro d'errore dei tool.
    /// </summary>
    public static IEndpointConventionBuilder MapTrasportoMcp(this WebApplication app)
    {
        app.UseWhen(
            static context => context.Request.Path.StartsWithSegments(EndpointPath),
            static branch => branch.Use(RequireActorAsync));

        return app.MapMcp(EndpointPath);
    }

    /// <summary>L'attore riconosciuto per la richiesta MCP in corso.</summary>
    public static Actor ActorOf(HttpContext http) =>
        http.Items[ActorItemKey] as Actor ?? throw new InvalidOperationException("Nessun attore: la richiesta non è passata dall'endpoint MCP.");

    private static async Task RequireActorAsync(HttpContext http, RequestDelegate next)
    {
        ActorResolver resolver = http.RequestServices.GetRequiredService<ActorResolver>();

        if (resolver.Resolve(http.Request.Headers[ActorResolver.ApiKeyHeaderName]) is not { } actor)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;

            await http.Response.WriteAsJsonAsync(new
            {
                error = new { code = ErrorCodes.Unauthorized, message = $"Intestazione {ActorResolver.ApiKeyHeaderName} assente o non riconosciuta." }
            });

            return;
        }

        http.Items[ActorItemKey] = actor;

        await next(http);
    }
}
