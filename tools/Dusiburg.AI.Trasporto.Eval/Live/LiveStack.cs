using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Agents.Authority;
using Dusiburg.AI.Trasporto.Agents.Mapping;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Role = Dusiburg.AI.Trasporto.Shared.Security.Role;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dusiburg.AI.Trasporto.Eval.Live;

/// <summary>Esito di una chiamata a un tool: il dato strutturato, oppure il codice d'errore dell'involucro <c>{ error }</c>.</summary>
public sealed record ToolCallOutcome(bool IsError, string? ErrorCode, string? ErrorMessage, JsonElement? Data);

/// <summary>
/// Lo stack avviato dall'AppHost, visto da fuori come lo vede un agente: i tool di <c>efti-mcp</c> con la chiave di un ruolo, e
/// l'audit di <c>efti-api</c>. Qui non c'è nessuna scorciatoia verso il database: permessi e registrazioni sono quelli veri.
/// </summary>
public sealed class LiveStack(EvalSettings settings) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(100) };

    public EvalSettings Settings { get; } = settings;

    /// <summary>Verifica che <c>efti-mcp</c> ed <c>efti-api</c> rispondano; altrimenti spiega come avviarli.</summary>
    public async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage api = await _http.GetAsync(new Uri(Settings.EftiApi, "/health"), cancellationToken);
            using HttpResponseMessage mcp = await _http.GetAsync(new Uri(Settings.EftiMcp, "/health"), cancellationToken);

            api.EnsureSuccessStatusCode();
            mcp.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"Lo stack non risponde ({Settings.EftiApi}, {Settings.EftiMcp}): avviare l'AppHost (dotnet run --project src/Dusiburg.AI.Trasporto.AppHost) " +
                $"e importare le spedizioni. Dettaglio: {exception.Message}");
        }
    }

    /// <summary>Nomi dei tool che <c>efti-mcp</c> mostra a chi ha quella chiave.</summary>
    public async Task<IReadOnlyList<string>> ListToolsAsync(string apiKey, CancellationToken cancellationToken)
    {
        await using McpClient client = await ConnectAsync(apiKey, cancellationToken);

        return [.. (await client.ListToolsAsync(cancellationToken: cancellationToken)).Select(tool => tool.Name).Order(StringComparer.Ordinal)];
    }

    public async Task<ToolCallOutcome> CallToolAsync(string apiKey, string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        await using McpClient client = await ConnectAsync(apiKey, cancellationToken);

        CallToolResult result = await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);

        if (result.IsError != true)
        {
            return new ToolCallOutcome(false, null, null, result.StructuredContent);
        }

        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

        try
        {
            ToolError? error = JsonSerializer.Deserialize<ToolErrorResponse>(text, TrasportoJson.Options)?.Error;

            return new ToolCallOutcome(true, error?.Code, error?.Message ?? text, null);
        }
        catch (JsonException)
        {
            // Un errore che non ha l'involucro dei nostri tool (per esempio "tool sconosciuto" del server MCP).
            return new ToolCallOutcome(true, null, text, null);
        }
    }

    /// <summary><c>tools/list</c> in JSON-RPC diretto e senza chiave: deve fermarsi a livello HTTP.</summary>
    public async Task<HttpStatusCode> ListToolsWithoutKeyAsync(string? apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Settings.EftiMcp)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.ParseAdd("application/json, text/event-stream");

        if (apiKey is not null)
        {
            request.Headers.Add(ActorResolver.ApiKeyHeaderName, apiKey);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);

        return response.StatusCode;
    }

    /// <summary>Eventi di audit rifiutati, dal più recente, come li legge l'amministratore.</summary>
    public async Task<IReadOnlyList<AuditEventView>> DeniedAuditEventsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Settings.EftiApi, $"/api/audit?outcome={AuditOutcome.Denied}"));

        request.Headers.Add(ActorResolver.ApiKeyHeaderName, Settings.ApiKey(Role.Admin));

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEventView>>(TrasportoJson.Options, cancellationToken) ?? [];
    }

    /// <summary>Gli agenti veri con il modello da misurare: i tool sono quelli di <c>efti-mcp</c>, aperti con la chiave di chi li avvia.</summary>
    public (MappingAgent Mapping, AuthorityAgent Authority) AgentsFor(ModelCandidate model)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [McpToolCatalog.UrlConfigurationKey] = Settings.EftiMcp.ToString() })
            .Build();
        var catalog = new McpToolCatalog(new PlainHttpClientFactory(), configuration, NullLoggerFactory.Instance);
        Ai.IChatClientFactory models = Settings.ModelsFor(model);

        return (new MappingAgent(models, catalog, NullLogger<MappingAgent>.Instance), new AuthorityAgent(models, catalog, NullLogger<AuthorityAgent>.Instance));
    }

    public void Dispose() => _http.Dispose();

    private async Task<McpClient> ConnectAsync(string apiKey, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = Settings.EftiMcp,
                AdditionalHeaders = new Dictionary<string, string> { [ActorResolver.ApiKeyHeaderName] = apiKey }
            },
            new HttpClient(),
            NullLoggerFactory.Instance,
            true);

        return await McpClient.CreateAsync(transport, null, NullLoggerFactory.Instance, cancellationToken);
    }

    private sealed class PlainHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
