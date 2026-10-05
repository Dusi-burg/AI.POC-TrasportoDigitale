using Dusiburg.AI.Trasporto.Mcp.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();

// Server MCP stateless su /mcp, ancora senza tool: i contratti della Fase 5 arrivano dopo ruoli e audit (Fase 4).
builder.AddTrasportoMcpServer();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Efti.Mcp", phase = 1, mcp = TrasportoMcpServerExtensions.EndpointPath });

app.MapTrasportoMcp();

app.Run();
