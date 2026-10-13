namespace Dusiburg.AI.Trasporto.Efti.Mcp.Tests.Support;

/// <summary>
/// Handler primario dell'<c>EftiApiClient</c> nei test: inoltra a <c>Efti.Api</c> in-process oppure, se
/// <see cref="Unavailable"/>, si comporta come se il servizio fosse fermo.
/// </summary>
internal sealed class UpstreamHandler(HttpMessageHandler eftiApi) : DelegatingHandler(eftiApi)
{
    public bool Unavailable { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Unavailable
            ? throw new HttpRequestException("Efti.Api finta non raggiungibile.")
            : base.SendAsync(request, cancellationToken);

    // L'handler è condiviso da tutta la classe di test, mentre HttpClientFactory ricicla le proprie catene:
    // la chiusura non deve propagarsi all'handler di Efti.Api.
    protected override void Dispose(bool disposing)
    {
    }
}
