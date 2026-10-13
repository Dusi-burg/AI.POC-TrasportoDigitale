using Dusiburg.AI.Trasporto.Shared.Errors;

namespace Dusiburg.AI.Trasporto.Mcp.Hosting;

/// <summary>
/// Errore di un tool MCP con un codice del catalogo <see cref="ErrorCodes"/>: il filtro sulle chiamate lo restituisce come
/// <c>{ error: { code, message } }</c>. Il messaggio arriva al modello: niente dettagli interni.
/// </summary>
public sealed class ToolException : Exception
{
    public ToolException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
