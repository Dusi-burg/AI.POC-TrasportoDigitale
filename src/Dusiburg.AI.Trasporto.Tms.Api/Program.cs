using Dusiburg.AI.Trasporto.Tms.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Schema tms del database Trasporto, creato da tools/Dusiburg.AI.Trasporto.DbInit: qui nessuna creazione né migrazione.
// L'integrazione Aspire aggiunge tracce SQL, health check del database e retry sugli errori transitori.
builder.AddSqlServerDbContext<TmsDbContext>("sql");

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Tms.Api", phase = 1 });

app.LogConnectionStringPresence("sql");

app.Run();
