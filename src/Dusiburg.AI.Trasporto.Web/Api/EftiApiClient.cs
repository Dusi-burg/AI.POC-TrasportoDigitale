using System.Net.Http.Json;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Serialization;

namespace Dusiburg.AI.Trasporto.Web.Api;

/// <summary>Client tipizzato verso <c>Efti.Api</c>: la Web non legge il database né chiama i modelli.</summary>
public sealed class EftiApiClient(HttpClient http)
{
    public const string BaseAddressConfigurationKey = "EftiApi:BaseAddress";

    /// <summary>Nome della risorsa nell'AppHost, risolto dal service discovery.</summary>
    public const string DefaultBaseAddress = "http://efti-api";

    public async Task<ProbeReport> GetHealthAsync(CancellationToken cancellationToken)
    {
        ProbeReport? report = await http.GetFromJsonAsync<ProbeReport>("api/health", TrasportoJson.Options, cancellationToken);

        return report ?? throw new InvalidOperationException("Efti.Api ha risposto senza contenuto a api/health.");
    }
}
