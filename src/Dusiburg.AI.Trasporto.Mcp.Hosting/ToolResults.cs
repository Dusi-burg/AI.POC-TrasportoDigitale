using System.Text.Encodings.Web;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Errors;
using ModelContextProtocol.Protocol;

namespace Dusiburg.AI.Trasporto.Mcp.Hosting;

/// <summary>
/// Risultati MCP di errore: <c>isError = true</c> e l'involucro <c>{ error: { code, message } }</c> come testo JSON, senza
/// <c>structuredContent</c>, che resta riservato ai risultati conformi all'<c>outputSchema</c> del tool.
/// </summary>
public static class ToolResults
{
    // Testo destinato al modello, non a una pagina HTML: le lettere accentate restano leggibili.
    private static readonly JsonSerializerOptions ErrorJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static CallToolResult Error(string code, string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(ToolErrorResponse.Create(code, message), ErrorJson) }]
    };
}
