using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Efti.Api.Health;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Schemi efti e audit del database Trasporto, creati da tools/Dusiburg.AI.Trasporto.DbInit: qui nessuna creazione né migrazione.
builder.AddSqlServerDbContext<EftiDbContext>(SqlHealthProbe.ConnectionStringName);
builder.AddSqlServerDbContext<AuditDbContext>(SqlHealthProbe.ConnectionStringName);

// Manopole dei modelli dall'AppHost; client di chat ed embedding ospitati qui come librerie (P3).
builder.Services.AddTrasportoAi(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IHealthProbe, SqlHealthProbe>();
builder.Services.AddSingleton<HealthService>();
builder.Services.AddSingleton<HealthReportCache>();
builder.Services.AddHealthChecks().AddCheck<TrasportoHealthCheck>(TrasportoHealthCheck.Name);

// JSON camelCase con enum come stringhe, uguale al client della Web (TrasportoJson).
builder.Services.ConfigureHttpJsonOptions(options => TrasportoJson.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Efti.Api", phase = 1 });

app.MapGroup("/api")
    .MapHealthEndpoints();

app.LogConnectionStringPresence(SqlHealthProbe.ConnectionStringName);

app.Run();

/// <summary>Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test.</summary>
public sealed class EftiApiEntryPoint;
