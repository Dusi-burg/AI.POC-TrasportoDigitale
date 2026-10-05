using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Efti.Api.Dev;
using Dusiburg.AI.Trasporto.Efti.Api.Health;
using Dusiburg.AI.Trasporto.Efti.Api.Shipments;
using Dusiburg.AI.Trasporto.Efti.Api.Tms;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Import;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Schemi efti e audit del database Trasporto, creati da tools/Dusiburg.AI.Trasporto.DbInit: qui nessuna creazione né migrazione.
builder.AddSqlServerDbContext<EftiDbContext>(SqlHealthProbe.ConnectionStringName);
builder.AddSqlServerDbContext<AuditDbContext>(SqlHealthProbe.ConnectionStringName);

// Manopole dei modelli dall'AppHost; client di chat ed embedding ospitati qui come librerie (P3).
builder.Services.AddTrasportoAi(builder.Configuration);

// Tms.Api tramite service discovery (AppHost: WithReference(tmsApi)); l'indirizzo si può sovrascrivere, ad esempio nei test.
builder.Services.AddHttpClient<TmsApiClient>((services, client) =>
    client.BaseAddress = new Uri(
        services.GetRequiredService<IConfiguration>()[TmsApiClient.BaseAddressConfigurationKey] ?? TmsApiClient.DefaultBaseAddress));

// Nucleo eFTI: schema letto una volta sola, mappatura e regole deterministiche (Efti.Core).
builder.Services.AddSingleton(_ => EftiSchemaCatalog.Default);
builder.Services.AddSingleton<EftiXmlExporter>();
builder.Services.AddSingleton<EftiXmlValidator>();
builder.Services.AddSingleton<EftiValidator>();
builder.Services.Configure<CarrierOptions>(builder.Configuration.GetSection(CarrierOptions.SectionName));
builder.Services.AddScoped<ShipmentImportService>();
builder.Services.AddScoped<ShipmentViewQueries>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IHealthProbe, SqlHealthProbe>();
builder.Services.AddSingleton<IHealthProbe, EftiSchemaHealthProbe>();
builder.Services.AddSingleton<HealthService>();
builder.Services.AddSingleton<HealthReportCache>();
builder.Services.AddHealthChecks().AddCheck<TrasportoHealthCheck>(TrasportoHealthCheck.Name);

// JSON camelCase con enum come stringhe, uguale ai client (TrasportoJson).
builder.Services.ConfigureHttpJsonOptions(options => TrasportoJson.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails(ApiProblems.Configure);
builder.Services.Configure<ExceptionHandlerOptions>(ApiProblems.ConfigureExceptionHandler);

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Efti.Api", phase = 3 });

app.MapGroup("/api")
    .MapHealthEndpoints()
    .MapShipmentEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapEftiDevEndpoints();
}

app.LogConnectionStringPresence(SqlHealthProbe.ConnectionStringName);

app.Run();

/// <summary>Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test.</summary>
public sealed class EftiApiEntryPoint;
