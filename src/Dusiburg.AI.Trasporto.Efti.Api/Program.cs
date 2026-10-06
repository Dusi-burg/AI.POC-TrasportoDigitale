using System.Globalization;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Documents.Extraction;
using Dusiburg.AI.Trasporto.Efti.Api.Audit;
using Dusiburg.AI.Trasporto.Efti.Api.Dev;
using Dusiburg.AI.Trasporto.Efti.Api.Documents;
using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Efti.Api.Gate;
using Dusiburg.AI.Trasporto.Efti.Api.Health;
using Dusiburg.AI.Trasporto.Efti.Api.Proposals;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.Efti.Api.Shipments;
using Dusiburg.AI.Trasporto.Efti.Api.Tms;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Gate;
using Dusiburg.AI.Trasporto.Efti.Core.Import;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
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

// Identità simulata, matrice dei ruoli e audit (Fase 4): ogni endpoint /api dichiara la propria operazione e passa da AccessFilter.
builder.Services.AddSingleton<ActorResolver>();
builder.Services.AddScoped<AuditNote>();
builder.Services.AddScoped<AuditTrail>();
builder.Services.AddScoped<AuditQueries>();

// Gate simulato: registro degli identificatori e sottoinsiemi dell'autorità, ricavati dallo schema.
builder.Services.AddSingleton(_ => EftiSubsetProjector.CreateDefault());
builder.Services.AddOptions<EftiPlatformOptions>()
    .Bind(builder.Configuration.GetSection(EftiPlatformOptions.SectionName))
    .Validate(
        options => options.GateRetentionMinutes >= 0,
        $"{EftiPlatformOptions.SectionName}:{nameof(EftiPlatformOptions.GateRetentionMinutes)} non può essere negativo.")
    .ValidateOnStart();
builder.Services.AddScoped<IdentifierRegistry>();

// Proposte di completamento (Fase 5): le lascia chi ha il permesso, anche un agente dal tool MCP; le decide una persona.
builder.Services.AddScoped<FieldProposalService>();

// Estrazione dai CMR (Fase 7): il modello legge, il codice calcola la confidenza e decide dove va ogni campo. Pesi e soglia
// dalla sezione Extraction; soglia e accettazione automatica anche dalle manopole dell'AppHost.
builder.Services.AddSingleton<CmrExtractor>();
builder.Services.AddSingleton<ExtractionAssessor>();
builder.Services.AddOptions<ExtractionOptions>()
    .Bind(builder.Configuration.GetSection(ExtractionOptions.SectionName))
    .Configure<IConfiguration>(static (options, configuration) =>
    {
        if (configuration[ExtractionOptions.ThresholdKey] is { Length: > 0 } threshold)
        {
            options.ReviewThreshold = decimal.TryParse(threshold, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                ? value
                : throw new InvalidOperationException($"{ExtractionOptions.ThresholdKey} '{threshold}' non valido: serve un numero fra 0 e 1, con il punto decimale.");
        }

        if (configuration[ExtractionOptions.AutoAcceptKey] is { Length: > 0 } autoAccept)
        {
            options.AutoAccept = bool.TryParse(autoAccept, out bool value)
                ? value
                : throw new InvalidOperationException($"{ExtractionOptions.AutoAcceptKey} '{autoAccept}' non valido: true oppure false.");
        }
    })
    .Validate(static options => options.IsValid(), $"Sezione {ExtractionOptions.SectionName} non valida: soglia fra 0 e 1, da 1 a 3 letture, pesi non negativi e non tutti a zero.")
    .ValidateOnStart();
builder.Services.AddScoped<DocumentService>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IHealthProbe, SqlHealthProbe>();
builder.Services.AddSingleton<IHealthProbe, EftiSchemaHealthProbe>();
builder.Services.AddSingleton<IHealthProbe, RoleKeysHealthProbe>();
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

app.MapGet("/", () => new { service = "Dusiburg.AI.Trasporto.Efti.Api", phase = 7 });

app.MapGroup("/api")
    .MapHealthEndpoints()
    .MapShipmentEndpoints()
    .MapProposalEndpoints()
    .MapDocumentEndpoints()
    .MapGateEndpoints()
    .MapAuditEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapEftiDevEndpoints();
}

app.LogConnectionStringPresence(SqlHealthProbe.ConnectionStringName);

app.Run();

/// <summary>Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test.</summary>
public sealed class EftiApiEntryPoint;
