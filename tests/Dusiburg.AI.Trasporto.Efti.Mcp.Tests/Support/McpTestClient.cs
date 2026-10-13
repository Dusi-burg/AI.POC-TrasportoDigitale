using System.Net;
using System.Text;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Mcp.Hosting;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dusiburg.AI.Trasporto.Efti.Mcp.Tests.Support;

/// <summary>Client MCP di test costruito sull'<see cref="HttpClient"/> di una <see cref="WebApplicationFactory{TEntryPoint}"/>.</summary>
internal static class McpTestClient
{
    public static async Task<McpClient> ConnectAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string apiKey, CancellationToken cancellationToken)
        where TEntryPoint : class
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(factory.Server.BaseAddress, TrasportoMcpServerExtensions.EndpointPath),
                AdditionalHeaders = new Dictionary<string, string> { [ActorResolver.ApiKeyHeaderName] = apiKey }
            },
            factory.CreateClient(),
            null,
            true);

        return await McpClient.CreateAsync(transport, null, null, cancellationToken);
    }

    /// <summary>Una chiamata a tool con un client dedicato (il server è stateless).</summary>
    public static async Task<CallToolResult> CallToolAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string apiKey, string toolName, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
        where TEntryPoint : class
    {
        await using McpClient client = await ConnectAsync(factory, apiKey, cancellationToken);

        return await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<string, McpClientTool>> ListToolsAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string apiKey, CancellationToken cancellationToken)
        where TEntryPoint : class
    {
        await using McpClient client = await ConnectAsync(factory, apiKey, cancellationToken);

        return (await client.ListToolsAsync(cancellationToken: cancellationToken)).ToDictionary(t => t.Name);
    }

    /// <summary><c>tools/list</c> in JSON-RPC diretto, senza client MCP: per verificare la chiave a livello HTTP.</summary>
    public static async Task<(HttpStatusCode Status, string? Code)> PostToolsListAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string? apiKey, CancellationToken cancellationToken)
        where TEntryPoint : class
    {
        using HttpClient http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, TrasportoMcpServerExtensions.EndpointPath)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json")
        };

        if (apiKey is not null)
        {
            request.Headers.Add(ActorResolver.ApiKeyHeaderName, apiKey);
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return (response.StatusCode,
            document.RootElement.TryGetProperty("error", out JsonElement error) && error.TryGetProperty("code", out JsonElement code) ? code.GetString() : null);
    }

    public static string Text(this CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    /// <summary>Output strutturato di un risultato positivo.</summary>
    public static T ReadStructured<T>(this CallToolResult result)
    {
        Assert.That(result.IsError, Is.Not.True, () => $"Il tool ha restituito un errore: {result.Text()}");
        Assert.That(result.StructuredContent, Is.Not.Null);

        return result.StructuredContent!.Value.Deserialize<T>(TrasportoJson.Options)!;
    }

    /// <summary>Errore di tool: <c>isError</c>, involucro come testo JSON e nessun <c>structuredContent</c>.</summary>
    public static ToolError ReadError(this CallToolResult result)
    {
        Assert.That(result.IsError, Is.True, () => $"Atteso un errore, ricevuto: {result.Text()}");
        Assert.That(result.StructuredContent, Is.Null);

        return JsonSerializer.Deserialize<ToolErrorResponse>(result.Text(), TrasportoJson.Options)!.Error;
    }

    public static IReadOnlyList<string> RequiredParameters(this McpClientTool tool) =>
        tool.ProtocolTool.InputSchema.TryGetProperty("required", out JsonElement required)
            ? [.. required.EnumerateArray().Select(e => e.GetString()!)]
            : [];
}
