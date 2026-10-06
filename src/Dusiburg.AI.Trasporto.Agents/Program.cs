using Dusiburg.AI.Trasporto.Agents.Authority;
using Dusiburg.AI.Trasporto.Agents.Endpoints;
using Dusiburg.AI.Trasporto.Agents.Mapping;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Gli agenti agiscono solo attraverso i tool MCP (P4): qui nessun accesso al database e nessuna identità propria.
builder.Services.AddTrasportoAi(builder.Configuration);

// Identità simulata: la chiave di chi chiama dice chi è; l'agente la porta con sé verso efti-mcp.
builder.Services.AddSingleton<ActorResolver>();

// efti-mcp tramite service discovery (AppHost: WithReference(eftiMcp)); l'indirizzo si può sovrascrivere, ad esempio nei test.
builder.Services.AddHttpClient(McpToolCatalog.HttpClientName);
builder.Services.AddSingleton<IAgentToolCatalog, McpToolCatalog>();
builder.Services.AddSingleton<MappingAgent>();
builder.Services.AddSingleton<AuthorityAgent>();

// JSON camelCase con enum come stringhe, uguale ai client (TrasportoJson).
builder.Services.ConfigureHttpJsonOptions(options => TrasportoJson.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails(ApiProblems.Configure);
builder.Services.Configure<ExceptionHandlerOptions>(ApiProblems.ConfigureExceptionHandler);

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Agents", phase = 6 });

app.MapAgentEndpoints();

app.Run();

/// <summary>Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test.</summary>
public sealed class AgentsEntryPoint;
