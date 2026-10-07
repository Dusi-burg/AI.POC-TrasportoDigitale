using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace Dusiburg.AI.Trasporto.Web.Api;

/// <summary>Filtri dell'elenco delle spedizioni, come li scrive chi usa la pagina.</summary>
public sealed record ShipmentSearch(string? Status, string? Customer, string? From, string? To);

/// <summary>Filtri dell'audit.</summary>
public sealed record AuditSearch(string? Role, string? Operation, string? Outcome, string? ShipmentCode);

/// <summary>
/// Client tipizzato verso <c>Efti.Api</c>: la Web non legge il database né chiama i modelli. Ogni richiesta porta la chiave del
/// ruolo scelto (<c>RoleKeyHandler</c>). Letture e decisioni brevi: usa la resilienza standard di ServiceDefaults.
/// </summary>
public sealed class EftiApiClient(HttpClient http)
{
    public const string BaseAddressConfigurationKey = "EftiApi:BaseAddress";

    /// <summary>Nome della risorsa nell'AppHost, risolto dal service discovery.</summary>
    public const string DefaultBaseAddress = "http://efti-api";

    public Task<ProbeReport> GetHealthAsync(CancellationToken cancellationToken) => GetAsync<ProbeReport>("api/health", cancellationToken);

    public Task<SettingsView> GetSettingsAsync(CancellationToken cancellationToken) => GetAsync<SettingsView>("api/settings", cancellationToken);

    public Task<List<ShipmentSummaryView>> ListShipmentsAsync(ShipmentSearch search, CancellationToken cancellationToken) =>
        GetAsync<List<ShipmentSummaryView>>(
            Query("api/views/shipments", ("status", search.Status), ("customer", search.Customer), ("from", search.From), ("to", search.To)), cancellationToken);

    public Task<ShipmentDetailView> GetShipmentAsync(string code, CancellationToken cancellationToken) =>
        GetAsync<ShipmentDetailView>($"api/views/shipments/{Uri.EscapeDataString(code)}", cancellationToken);

    /// <summary>Il dataset in XML eFTI, con il suo stato (intestazione <c>X-Efti-Dataset-Status</c>).</summary>
    public async Task<(string Xml, string? DatasetStatus)> GetDatasetXmlAsync(string code, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync($"api/shipments/{Uri.EscapeDataString(code)}/dataset.xml", cancellationToken);

        await ApiProblemException.ThrowIfErrorAsync(response, cancellationToken);

        return (await response.Content.ReadAsStringAsync(cancellationToken),
            response.Headers.TryGetValues("X-Efti-Dataset-Status", out IEnumerable<string>? values) ? values.FirstOrDefault() : null);
    }

    /// <summary>Il documento eCMR della spedizione: stato, indicazioni mancanti e contenuto.</summary>
    public Task<EcmrDocumentView> GetEcmrAsync(string code, CancellationToken cancellationToken) =>
        GetAsync<EcmrDocumentView>($"api/shipments/{Uri.EscapeDataString(code)}/ecmr", cancellationToken);

    /// <summary>L'eCMR in XML UN/CEFACT, con il suo stato (intestazione <c>X-Ecmr-Status</c>).</summary>
    public async Task<(string Xml, string? EcmrStatus)> GetEcmrXmlAsync(string code, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync($"api/shipments/{Uri.EscapeDataString(code)}/ecmr.xml", cancellationToken);

        await ApiProblemException.ThrowIfErrorAsync(response, cancellationToken);

        return (await response.Content.ReadAsStringAsync(cancellationToken),
            response.Headers.TryGetValues("X-Ecmr-Status", out IEnumerable<string>? values) ? values.FirstOrDefault() : null);
    }

    public Task<List<FieldProposalView>> ListProposalsAsync(string code, CancellationToken cancellationToken) =>
        GetAsync<List<FieldProposalView>>($"api/shipments/{Uri.EscapeDataString(code)}/proposals", cancellationToken);

    public Task<ProposalDecisionResponse> DecideProposalAsync(int proposalId, ProposalStatus decision, string? note, CancellationToken cancellationToken) =>
        PostAsync<ProposalDecisionResponse>($"api/proposals/{proposalId}/decision", new ProposalDecisionRequest(decision, note), cancellationToken);

    public Task<List<DocumentSummaryView>> ListDocumentsAsync(string code, CancellationToken cancellationToken) =>
        GetAsync<List<DocumentSummaryView>>($"api/shipments/{Uri.EscapeDataString(code)}/documents", cancellationToken);

    public Task<DocumentDetailView> GetDocumentAsync(int documentId, CancellationToken cancellationToken) =>
        GetAsync<DocumentDetailView>($"api/documents/{documentId}", cancellationToken);

    public Task<List<ReviewTaskView>> ListReviewTasksAsync(string? status, string? shipmentCode, CancellationToken cancellationToken) =>
        GetAsync<List<ReviewTaskView>>(Query("api/review-tasks", ("status", status), ("shipmentCode", shipmentCode)), cancellationToken);

    public Task<ReviewDecisionResponse> DecideReviewAsync(int reviewTaskId, ReviewStatus decision, string? value, string? note, CancellationToken cancellationToken) =>
        PostAsync<ReviewDecisionResponse>($"api/review-tasks/{reviewTaskId}/decision", new ReviewDecisionRequest(decision, value, note), cancellationToken);

    public Task<List<AuditEventView>> ListAuthorityAccessesAsync(string code, CancellationToken cancellationToken) =>
        GetAsync<List<AuditEventView>>($"api/shipments/{Uri.EscapeDataString(code)}/authority-access", cancellationToken);

    /// <param name="at">Momento del controllo, se simulato; <c>null</c> = adesso.</param>
    public Task<List<ConsignmentIdentifiersView>> FindIdentifiersAsync(string plate, DateTimeOffset? at, CancellationToken cancellationToken) =>
        GetAsync<List<ConsignmentIdentifiersView>>(
            Query("api/gate/identifiers", ("plate", plate), ("at", at?.ToString("O", System.Globalization.CultureInfo.InvariantCulture))), cancellationToken);

    /// <summary>Aiuto per la demo: i momenti recenti in cui simulare un controllo per quella targa.</summary>
    public Task<ControlMomentsView> GetControlMomentsAsync(string plate, CancellationToken cancellationToken) =>
        GetAsync<ControlMomentsView>(Query("api/gate/control-moments", ("plate", plate)), cancellationToken);

    public Task<AuthoritySubsetView> GetSubsetAsync(string uil, string subsetId, CancellationToken cancellationToken) =>
        GetAsync<AuthoritySubsetView>(Query("api/gate/subset", ("uil", uil), ("subsetId", subsetId)), cancellationToken);

    public Task<List<AuditEventView>> ListAuditAsync(AuditSearch search, CancellationToken cancellationToken) =>
        GetAsync<List<AuditEventView>>(
            Query("api/audit", ("role", search.Role), ("operation", search.Operation), ("outcome", search.Outcome), ("shipmentCode", search.ShipmentCode)),
            cancellationToken);

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken);

        return await ApiProblemException.ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string uri, object body, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(uri, body, TrasportoJson.Options, cancellationToken);

        return await ApiProblemException.ReadAsync<T>(response, cancellationToken);
    }

    /// <summary>Solo i filtri valorizzati finiscono nella query.</summary>
    private static string Query(string path, params (string Name, string? Value)[] parameters) =>
        QueryHelpers.AddQueryString(path, parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => new KeyValuePair<string, string?>(p.Name, p.Value!.Trim())));
}

/// <summary>
/// Le operazioni lunghe di <c>Efti.Api</c>: l'importazione di tutti gli ordini e il caricamento di un documento, la cui
/// estrazione gira nella stessa richiesta e con il modello locale dura decine di secondi. È un client a parte: niente resilienza
/// standard (taglierebbe il tentativo a 10 s e lo ripeterebbe), timeout lungo.
/// </summary>
public sealed class EftiLongRunningClient(HttpClient http)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>Importa tutti gli ordini del TMS non ancora importati.</summary>
    public async Task<ImportShipmentsResponse> ImportAllAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsync("api/shipments/import", content: null, cancellationToken);

        return await ApiProblemException.ReadAsync<ImportShipmentsResponse>(response, cancellationToken);
    }

    public async Task<DocumentDetailView> UploadAsync(string shipmentCode, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var file = new ByteArrayContent(content);

        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var form = new MultipartFormDataContent { { file, "file", fileName } };
        using HttpResponseMessage response = await http.PostAsync($"api/shipments/{Uri.EscapeDataString(shipmentCode)}/documents", form, cancellationToken);

        return await ApiProblemException.ReadAsync<DocumentDetailView>(response, cancellationToken);
    }
}

/// <summary>
/// Client verso il servizio <c>Agents</c>, solo per gli stream: un run dura da decine di secondi a qualche minuto, quindi niente
/// resilienza standard e timeout lungo. La risposta si legge mentre arriva.
/// </summary>
public sealed class AgentsApiClient(HttpClient http)
{
    public const string BaseAddressConfigurationKey = "AgentsApi:BaseAddress";

    public const string DefaultBaseAddress = "http://agents";

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public const string MappingStreamPath = "api/agents/mapping/run/stream";

    public const string AuthorityStreamPath = "api/agents/authority/ask/stream";

    /// <summary>Apre lo stream di un agente; chi chiama legge il corpo e chiude la risposta.</summary>
    public Task<HttpResponseMessage> OpenStreamAsync(string path, object request, CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request, request.GetType(), options: TrasportoJson.Options) };

        return http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
