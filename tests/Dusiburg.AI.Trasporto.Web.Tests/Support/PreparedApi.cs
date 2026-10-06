using System.Net;
using System.Text;
using System.Text.Json;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;

namespace Dusiburg.AI.Trasporto.Web.Tests.Support;

/// <summary>Una richiesta arrivata a valle: ciò che i test controllano di come la Web chiama i servizi.</summary>
internal sealed record DownstreamCall(HttpMethod Method, string Path, string Query, string? ApiKey, string Body);

/// <summary>
/// <c>Efti.Api</c> e <c>Agents</c> sostituite da risposte preparate per verbo e percorso. Un percorso non preparato risponde 404
/// (la resilienza standard non lo ritenta); <see cref="UnreachablePath"/> fa fallire un percorso come un servizio spento, da
/// usare solo con i client senza resilienza: quella standard ritenterebbe con attese crescenti.
/// </summary>
internal sealed class PreparedApi : HttpMessageHandler
{
    private readonly Dictionary<(HttpMethod Method, string Path), Func<HttpResponseMessage>> _responses = [];
    private readonly List<DownstreamCall> _calls = [];

    /// <summary>Il percorso che risponde come un servizio spento; <c>null</c> = nessuno.</summary>
    public string? UnreachablePath { get; set; }

    public IReadOnlyList<DownstreamCall> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public IReadOnlyList<DownstreamCall> CallsTo(string path) => [.. Calls.Where(call => call.Path == path)];

    public PreparedApi Get(string path, object data) => Json(HttpMethod.Get, path, data);

    public PreparedApi Post(string path, object data) => Json(HttpMethod.Post, path, data);

    public PreparedApi Text(string path, string content, string mediaType, params (string Name, string Value)[] headers) =>
        Respond(HttpMethod.Get, path, () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, mediaType) };

            foreach ((string name, string value) in headers)
            {
                response.Headers.Add(name, value);
            }

            return response;
        });

    public PreparedApi Stream(string path, string sse) =>
        Respond(HttpMethod.Post, path, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });

    /// <summary>Un rifiuto o un errore nel formato dei servizi: ProblemDetails con titolo e dettaglio.</summary>
    public PreparedApi Problem(HttpMethod method, string path, HttpStatusCode status, string title, string detail) =>
        Respond(method, path, () => new HttpResponseMessage(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { title, detail, status = (int)status, code = "unauthorized" }, TrasportoJson.Options), Encoding.UTF8, "application/problem+json")
        });

    private PreparedApi Json(HttpMethod method, string path, object data) =>
        Respond(method, path, () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(data, data.GetType(), TrasportoJson.Options), Encoding.UTF8, "application/json")
        });

    private PreparedApi Respond(HttpMethod method, string path, Func<HttpResponseMessage> response)
    {
        _responses[(method, path)] = response;

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri!;
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        string? apiKey = request.Headers.TryGetValues(ActorResolver.ApiKeyHeaderName, out IEnumerable<string>? values) ? values.Single() : null;

        lock (_calls)
        {
            _calls.Add(new DownstreamCall(request.Method, uri.AbsolutePath, Uri.UnescapeDataString(uri.Query), apiKey, body));
        }

        if (uri.AbsolutePath == UnreachablePath)
        {
            throw new HttpRequestException("Connessione rifiutata (servizio di prova spento)");
        }

        HttpResponseMessage response = _responses.TryGetValue((request.Method, uri.AbsolutePath), out Func<HttpResponseMessage>? prepared)
            ? prepared()
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        response.RequestMessage = request;

        return response;
    }
}
