namespace Dusiburg.AI.Trasporto.Shared.Errors;

/// <summary>Codici d'errore comuni ad API e tool MCP: estensione <c>code</c> dei ProblemDetails, <c>error.code</c> nei tool.</summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";
    public const string Internal = "INTERNAL";
}

/// <summary>Errore di un tool MCP: il codice è uno di <see cref="ErrorCodes"/>, il messaggio arriva al modello.</summary>
public sealed record ToolError(string Code, string Message);

/// <summary>Involucro <c>{ error: { code, message } }</c> restituito dai tool con <c>isError = true</c>.</summary>
public sealed record ToolErrorResponse(ToolError Error)
{
    public static ToolErrorResponse Create(string code, string message) => new(new ToolError(code, message));
}
