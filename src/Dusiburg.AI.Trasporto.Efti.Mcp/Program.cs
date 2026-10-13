using Dusiburg.AI.Trasporto.Efti.Mcp.Efti;
using Dusiburg.AI.Trasporto.Efti.Mcp.Tools;
using Dusiburg.AI.Trasporto.Mcp.Hosting;
using Dusiburg.AI.Trasporto.Shared.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();

// Efti.Api tramite service discovery (AppHost: WithReference(eftiApi)); l'indirizzo si può sovrascrivere, ad esempio nei test.
// Il server MCP non ha database né identità propria: ogni richiesta porta a valle la chiave di chi ha chiamato il tool.
builder.Services.AddHttpClient<EftiApiClient>((services, client) =>
    client.BaseAddress = new Uri(
        services.GetRequiredService<IConfiguration>()[EftiApiClient.BaseAddressConfigurationKey] ?? EftiApiClient.DefaultBaseAddress));

// L'elenco dei tool si filtra con ciò che Efti.Api dice che il ruolo può fare: la matrice dei ruoli resta in un posto solo.
builder.Services.AddTransient<ICallerPermissions>(services => services.GetRequiredService<EftiApiClient>());

// Server MCP stateless su /mcp, raggiungibile solo con la chiave di un ruolo (ApiKeys__<Ruolo>, passate dall'AppHost).
builder.AddTrasportoMcpServer<EftiTools>(TrasportoTelemetry.Sources.McpEfti);

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Efti.Mcp", phase = 5, mcp = TrasportoMcpServerExtensions.EndpointPath });

app.MapTrasportoMcp();

app.Run();
