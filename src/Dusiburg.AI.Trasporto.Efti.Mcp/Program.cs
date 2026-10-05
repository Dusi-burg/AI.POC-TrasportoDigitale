using Dusiburg.AI.Trasporto.Mcp.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();

// Server MCP stateless su /mcp, raggiungibile solo con la chiave di un ruolo (ApiKeys__<Ruolo>, passate dall'AppHost).
// Ancora senza tool: i contratti arrivano con la Fase 5.
builder.AddTrasportoMcpServer();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Efti.Mcp", phase = 1, mcp = TrasportoMcpServerExtensions.EndpointPath });

app.MapTrasportoMcp();

app.Run();
