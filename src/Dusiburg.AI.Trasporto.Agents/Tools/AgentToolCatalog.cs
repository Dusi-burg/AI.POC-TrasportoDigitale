using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Dusiburg.AI.Trasporto.Agents.Tools;

/// <summary>I tool che un run può usare, aperti con la chiave di chi lo ha avviato; si chiudono a fine run.</summary>
public interface IAgentToolSession : IAsyncDisposable
{
    IReadOnlyList<AIFunction> Tools { get; }
}

public interface IAgentToolCatalog
{
    /// <summary>
    /// Apre i tool di <c>efti-mcp</c> come li vede chi ha quella chiave: il server mostra a ogni ruolo solo i propri, quindi un
    /// agente non ha mai più tool di chi lo usa.
    /// </summary>
    Task<IAgentToolSession> OpenAsync(string apiKey, CancellationToken cancellationToken);
}

/// <summary>Gli mancano i tool: il ruolo di chi ha avviato l'agente non può fare ciò che l'agente fa.</summary>
public sealed class AgentUnavailableException(string message) : Exception(message);

/// <summary>
/// Tool del server MCP eFTI. Una connessione per run, con la chiave di chi chiama: il server è stateless, e un client tenuto
/// aperto fra un run e l'altro porterebbe con sé la chiave del primo utente.
/// </summary>
public sealed class McpToolCatalog(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILoggerFactory loggerFactory) : IAgentToolCatalog
{
    public const string UrlConfigurationKey = "EftiMcp:Url";

    /// <summary>Risorsa <c>efti-mcp</c> dell'AppHost, risolta dal service discovery.</summary>
    public const string DefaultUrl = "http://efti-mcp/mcp";

    public const string HttpClientName = "efti-mcp";

    public async Task<IAgentToolSession> OpenAsync(string apiKey, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(configuration[UrlConfigurationKey] ?? DefaultUrl),
                Name = HttpClientName,
                AdditionalHeaders = new Dictionary<string, string> { [ActorResolver.ApiKeyHeaderName] = apiKey }
            },
            httpClientFactory.CreateClient(HttpClientName),
            loggerFactory,
            false);

        McpClient client = await McpClient.CreateAsync(transport, null, loggerFactory, cancellationToken);

        try
        {
            return new Session(client, [.. await client.ListToolsAsync(cancellationToken: cancellationToken)]);
        }
        catch
        {
            await client.DisposeAsync();

            throw;
        }
    }

    private sealed class Session(McpClient client, IReadOnlyList<AIFunction> tools) : IAgentToolSession
    {
        public IReadOnlyList<AIFunction> Tools { get; } = tools;

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}

public static class AgentToolSessionExtensions
{
    /// <summary>
    /// I tool dell'agente fra quelli che la chiave vede, ciascuno dietro la sua guardia. Se ne manca uno l'agente non parte:
    /// meglio un rifiuto chiaro che un modello che improvvisa senza gli strumenti.
    /// </summary>
    public static List<AITool> Guarded(this IAgentToolSession session, AgentRun run, IReadOnlyDictionary<string, ToolRule> rules)
    {
        string[] missing = [.. run.AllowedTools.Where(name => session.Tools.All(tool => tool.Name != name)).Order()];

        if (missing.Length > 0)
        {
            throw new AgentUnavailableException(
                $"{run.AgentName} non può partire: al ruolo di chi lo avvia mancano i tool {string.Join(", ", missing)}.");
        }

        return
        [
            .. session.Tools
                .Where(tool => run.AllowedTools.Contains(tool.Name))
                .Select(tool => (AITool)new GuardedTool(tool, run, rules.GetValueOrDefault(tool.Name)))
        ];
    }
}
