using Dusiburg.AI.Trasporto.Ai;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Gli agenti agiscono solo attraverso i tool MCP (P4): qui nessun accesso al database. Agenti ed endpoint arrivano con la Fase 6.
builder.Services.AddTrasportoAi(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Agents", phase = 1 });

app.Run();
