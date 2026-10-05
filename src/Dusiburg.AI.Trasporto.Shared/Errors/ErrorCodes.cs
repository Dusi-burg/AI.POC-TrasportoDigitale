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
