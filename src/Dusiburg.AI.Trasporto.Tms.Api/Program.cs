using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Tms.Api.Dev;
using Dusiburg.AI.Trasporto.Tms.Api.Views;
using Dusiburg.AI.Trasporto.Tms.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Schema tms del database Trasporto, creato da tools/Dusiburg.AI.Trasporto.DbInit: qui nessuna creazione né migrazione.
// L'integrazione Aspire aggiunge tracce SQL, health check del database e retry sugli errori transitori.
builder.AddSqlServerDbContext<TmsDbContext>("sql");

// JSON camelCase con enum come stringhe, uguale al client di Efti.Api (TrasportoJson).
builder.Services.ConfigureHttpJsonOptions(options => TrasportoJson.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails(ApiProblems.Configure);
builder.Services.Configure<ExceptionHandlerOptions>(ApiProblems.ConfigureExceptionHandler);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TmsViewQueries>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Tms.Api", phase = 2 });

app.MapGroup("/api")
    .MapTmsViewEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapTmsDevEndpoints();
}

app.LogConnectionStringPresence("sql");

app.Run();

/// <summary>Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test.</summary>
public sealed class TmsApiEntryPoint;
