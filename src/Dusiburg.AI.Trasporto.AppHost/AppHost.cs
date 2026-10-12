var builder = DistributedApplication.CreateBuilder(args);

// Risorse esterne (G9): nessun container. Valore negli user-secrets dell'AppHost:
// ConnectionStrings:sql = Server=(localdb)\localdev;Database=Trasporto;Trusted_Connection=True;TrustServerCertificate=True
var sql = builder.AddConnectionString("sql");

// Porte fisse dai launchSettings (P8): Tms.Api 5301, Efti.Api 5302, Efti.Mcp 5303, Agents 5304, Web 5306; 5305 resta a
// Claims.Mcp (Release 2). O2C usa 5101-5106 e Sinistri 5201-5202, così le tre demo possono convivere.
var tmsApi = builder.AddProject<Projects.Dusiburg_AI_Trasporto_Tms_Api>("tms-api")
    .WithReference(sql)
    .WithHttpHealthCheck("/health");

var eftiApi = builder.AddProject<Projects.Dusiburg_AI_Trasporto_Efti_Api>("efti-api")
    .WithReference(sql)
    .WithReference(tmsApi)
    .WithHttpHealthCheck("/health");

var eftiMcp = builder.AddProject<Projects.Dusiburg_AI_Trasporto_Efti_Mcp>("efti-mcp")
    .WithReference(eftiApi)
    .WithHttpHealthCheck("/health");

var agents = builder.AddProject<Projects.Dusiburg_AI_Trasporto_Agents>("agents")
    .WithReference(eftiMcp)
    .WithHttpHealthCheck("/health");

// Manopole della demo (PLAN §8): si impostano sull'AppHost — riga di comando, user-secrets o variabili d'ambiente — e
// arrivano ai servizi che usano i modelli; senza valore vale il default del codice (AiOptions).
string[] modelSettings =
[
    "OLLAMA_ENDPOINT",
    "OLLAMA_CHAT_MODEL",
    "OLLAMA_NUM_CTX",
    "ANTHROPIC_MODEL",
    "ANTHROPIC_JUDGE_MODEL",
    "MODEL_EXTRACTION_PROVIDER",
    "MODEL_MAPPING_PROVIDER",
    "MODEL_DRAFT_PROVIDER",
    "MODEL_JUDGE_PROVIDER",
    "MODEL_AUTHORITY_PROVIDER",
    "EMBEDDING_MODEL",
    "EMBEDDING_DIMENSIONS",
    "TRASPORTO_PROMPT_CAPTURE_DIR"
];

eftiApi.WithConfigurationEnvironment(builder, modelSettings);

// Gate simulato: per quanti minuti dopo la consegna una spedizione resta raggiungibile da un'autorità (default del codice: 7200, cinque giorni).
eftiApi.WithConfigurationEnvironment(builder, "Efti__GateRetentionMinutes");

// Controllo su strada: nella realtà la ricerca per targa vale per ciò che è a bordo adesso. I dati della demo hanno date fisse,
// quindi qui si può indicare un altro momento recente (default dell'AppHost: acceso; default del codice: spento).
eftiApi.WithEnvironment("Efti__SimulatedControlTime", builder.Configuration["Efti:SimulatedControlTime"] is { Length: > 0 } simulated ? simulated : "true");

// Estrazione dai CMR: soglia sotto cui un campo va in revisione (default del codice: 0,80) e accettazione automatica delle
// proposte sopra soglia (default: spenta).
eftiApi.WithConfigurationEnvironment(builder, "EXTRACTION_REVIEW_THRESHOLD", "EXTRACTION_AUTO_ACCEPT");

// Letture del documento per provider del modello (default del codice: due in locale, una sul cloud; misura della Fase 9).
eftiApi.WithConfigurationEnvironment(builder, "Extraction__PassesByProvider__ollama", "Extraction__PassesByProvider__anthropic");
agents.WithConfigurationEnvironment(builder, modelSettings);

// Ollama (G9) non è un container: è quello installato sul PC. Se all'avvio non risponde, l'AppHost lo avvia come propria
// risorsa ("ollama serve") e lo ferma quando si chiude; se è già in esecuzione non lo tocca. Vale solo per un Ollama su questo
// PC: un OLLAMA_ENDPOINT remoto non si può avviare da qui. Il default è quello di AiOptions.
var ollamaEndpoint = new Uri(builder.Configuration["OLLAMA_ENDPOINT"] is { Length: > 0 } configured ? configured : "http://127.0.0.1:11434");

if (builder.ExecutionContext.IsRunMode && ollamaEndpoint.IsLoopback && !AppHostExtensions.IsListening(ollamaEndpoint))
{
    string ollamaHost = $"{ollamaEndpoint.Host}:{ollamaEndpoint.Port}";

    var ollama = builder.AddExecutable("ollama", "ollama", ".", "serve")
        .WithEnvironment("OLLAMA_HOST", ollamaHost);

    eftiApi.WaitFor(ollama);
    agents.WaitFor(ollama);
}

// Chiave del modello Claude: Parameters:anthropic-api-key. Il parametro si dichiara solo se valorizzato, così l'AppHost parte
// anche senza chiave: nella Release 1 i compiti girano in locale di default e /api/health segnala la mancanza.
if (builder.Configuration["Parameters:anthropic-api-key"] is { Length: > 0 })
{
    var anthropicApiKey = builder.AddParameter("anthropic-api-key", secret: true);

    eftiApi.WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey);
    agents.WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey);
}

// UI: solo HTTP verso i servizi proprietari dei dati, nessun riferimento a sql.
var web = builder.AddProject<Projects.Dusiburg_AI_Trasporto_Web>("web")
    .WithReference(eftiApi)
    .WithReference(tmsApi)
    .WithReference(agents)
    .WithHttpHealthCheck("/health");

// Link "apri la traccia" di audit e accessi dell'autorità: primo indirizzo del dashboard dai launchSettings dell'AppHost.
if (builder.Configuration["ASPNETCORE_URLS"]?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } dashboard)
{
    web.WithEnvironment("TRASPORTO_DASHBOARD_URL", dashboard);
}

// Identità simulata (G12): una chiave per ruolo, Parameters:api-key-<ruolo> negli user-secrets. Efti.Api ed Efti.Mcp le
// usano per riconoscere chi chiama; Agents e Web per chiamare con il ruolo dell'utente. Un ruolo senza chiave non si dichiara:
// l'AppHost parte lo stesso e /api/health dice quale manca.
foreach (string role in new[] { "Dispatcher", "ClaimsManager", "Authority", "Admin" })
{
    string parameterName = $"api-key-{role.ToLowerInvariant()}";

    if (builder.Configuration[$"Parameters:{parameterName}"] is { Length: > 0 })
    {
        var apiKey = builder.AddParameter(parameterName, secret: true);

        foreach (var service in new[] { eftiApi, eftiMcp, agents, web })
        {
            service.WithEnvironment($"ApiKeys__{role}", apiKey);
        }
    }
}

builder.Build().Run();

internal static class AppHostExtensions
{
    /// <summary>Qualcuno ascolta già a quell'indirizzo? Una prova di connessione breve, fatta una volta all'avvio.</summary>
    public static bool IsListening(Uri endpoint)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();

            return client.ConnectAsync(endpoint.Host, endpoint.Port).Wait(TimeSpan.FromSeconds(1)) && client.Connected;
        }
        catch (Exception exception) when (exception is AggregateException or System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Inoltra al servizio le impostazioni presenti nella configurazione dell'AppHost, saltando quelle non valorizzate:
    /// così una variabile della demo si imposta in un solo posto e non va replicata in ogni progetto.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithConfigurationEnvironment(
        this IResourceBuilder<ProjectResource> resource, IDistributedApplicationBuilder builder, params string[] names)
    {
        foreach (var name in names)
        {
            // Nelle variabili d'ambiente la sezione si scrive con il doppio underscore; nella configurazione con i due punti.
            if (builder.Configuration[name.Replace("__", ":", StringComparison.Ordinal)] is { Length: > 0 } value)
            {
                resource.WithEnvironment(name, value);
            }
        }

        return resource;
    }
}
