using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dusiburg.AI.Trasporto.Mcp.Hosting;

/// <summary>
/// Composizione comune dei server MCP del POC: trasporto HTTP stateless su <c>/mcp</c>, raggiungibile solo con la chiave di un
/// ruolo, elenco dei tool filtrato per ruolo, span ed errori strutturati su ogni chiamata.
/// </summary>
public static class TrasportoMcpServerExtensions
{
    public const string EndpointPath = "/mcp";

    private static readonly object ActorItemKey = new();

    /// <summary>
    /// Opzioni JSON dei tool (argomenti, output strutturato e schemi): le stesse regole dei contratti HTTP
    /// (<see cref="TrasportoJson"/>: camelCase, enum come stringhe), non quelle dell'SDK, con cui le proprietà <c>null</c>
    /// sparirebbero dall'output.
    /// </summary>
    public static JsonSerializerOptions ToolSerializerOptions { get; } = CreateToolSerializerOptions();

    /// <summary>
    /// Registra il server MCP con i tool di <typeparamref name="TTools"/>. Ogni tool dichiara la propria operazione con
    /// <see cref="ToolOperationAttribute"/>; chi ospita il server registra un <see cref="ICallerPermissions"/>.
    /// </summary>
    public static IMcpServerBuilder AddTrasportoMcpServer<TTools>(this IHostApplicationBuilder builder, string activitySourceName)
        where TTools : class
    {
        builder.Services.TryAddSingleton<ActorResolver>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(ToolOperationMap.From(typeof(TTools)));

        var activitySource = new ActivitySource(activitySourceName);

        return builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithRequestFilters(filters =>
            {
                filters.AddListToolsFilter(ToolListFilter.Create());
                filters.AddCallToolFilter(ToolCallFilter.Create(activitySource));
            })
            .WithTools<TTools>(ToolSerializerOptions);
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

            await http.Response.WriteAsJsonAsync(
                ToolErrorResponse.Create(ErrorCodes.Unauthorized, $"Intestazione {ActorResolver.ApiKeyHeaderName} assente o non riconosciuta."));

            return;
        }

        http.Items[ActorItemKey] = actor;

        await next(http);
    }

    private static JsonSerializerOptions CreateToolSerializerOptions()
    {
        JsonSerializerOptions options = TrasportoJson.Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        });

        options.MakeReadOnly();

        return options;
    }
}
