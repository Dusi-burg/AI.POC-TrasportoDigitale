using System.Text.Encodings.Web;
using System.Text.Unicode;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Web.Agents;
using Dusiburg.AI.Trasporto.Web.Api;
using Dusiburg.AI.Trasporto.Web.Demo;
using Dusiburg.AI.Trasporto.Web.Identity;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Identità simulata (G12): il ruolo scelto nel selettore sta in un cookie, e ogni chiamata a valle porta la chiave di quel
// ruolo (ApiKeys__<Ruolo>, passate dall'AppHost). La Web non ha permessi propri.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<RoleKeyHandler>();

// Servizi a valle tramite service discovery (AppHost: WithReference); gli indirizzi si possono sovrascrivere, ad esempio nei
// test. Tre client:
// - letture e decisioni brevi verso Efti.Api, con la resilienza standard di ServiceDefaults;
// - caricamento dei documenti (l'estrazione dura decine di secondi) e stream degli agenti (minuti): niente resilienza, che
//   taglierebbe il tentativo dopo pochi secondi e lo ripeterebbe, e timeout lungo.
string EftiApiAddress(IServiceProvider services) =>
    services.GetRequiredService<IConfiguration>()[EftiApiClient.BaseAddressConfigurationKey] ?? EftiApiClient.DefaultBaseAddress;

builder.Services.AddHttpClient<EftiApiClient>((services, client) => client.BaseAddress = new Uri(EftiApiAddress(services)))
    .AddHttpMessageHandler<RoleKeyHandler>();

// RemoveAllResilienceHandlers è marcata sperimentale (Microsoft.Extensions.Http.Resilience 10.10): l'alternativa sarebbe
// riconfigurare per nome la pipeline standard di ogni client, più fragile.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<EftiLongRunningClient>((services, client) =>
    {
        client.BaseAddress = new Uri(EftiApiAddress(services));
        client.Timeout = EftiLongRunningClient.Timeout;
    })
    .RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<RoleKeyHandler>();

builder.Services.AddHttpClient<AgentsApiClient>((services, client) =>
    {
        client.BaseAddress = new Uri(services.GetRequiredService<IConfiguration>()[AgentsApiClient.BaseAddressConfigurationKey] ?? AgentsApiClient.DefaultBaseAddress);
        client.Timeout = AgentsApiClient.Timeout;
    })
    .RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<RoleKeyHandler>();
#pragma warning restore EXTEXP0001

builder.Services.AddSingleton<DemoCmrFiles>();
builder.Services.Configure<DashboardOptions>(builder.Configuration);

// JSON del corpo inviato dagli script delle pagine: stesse regole delle API (camelCase, enum come stringhe).
builder.Services.ConfigureHttpJsonOptions(options => TrasportoJson.Configure(options.SerializerOptions));

// Testi in italiano: nelle pagine le lettere accentate restano leggibili invece di diventare entità numeriche.
builder.Services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthorization();

app.MapDefaultEndpoints();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapAgentStreams();

// I CMR sintetici della demo: file del repository, copiati accanto alla Web.
app.MapGet(DemoCmrFiles.RoutePattern, (string code, DemoCmrFiles files) =>
    files.Read(code) is { } pdf ? Results.File(pdf, "application/pdf", $"{code}.pdf") : Results.NotFound());

app.Run();

/// <summary>Tipo di riferimento dell'assembly per <c>WebApplicationFactory</c> nei test.</summary>
public sealed class WebEntryPoint;
