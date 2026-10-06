using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Web.Api;

/// <summary>Come si mostrano stati, esiti e numeri: etichette in italiano e classi dei badge. Nessuna logica di dominio.</summary>
public static class Presentation
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Label(DatasetStatus status) => status switch
    {
        DatasetStatus.Incomplete => "Incompleto",
        DatasetStatus.Valid => "Valido",
        DatasetStatus.Signed => "Firmato",
        _ => status.ToString()
    };

    public static string BadgeClass(DatasetStatus status) => status switch
    {
        DatasetStatus.Valid => "text-bg-success",
        DatasetStatus.Signed => "text-bg-primary",
        _ => "text-bg-warning"
    };

    public static string Label(IssueSeverity severity) => severity == IssueSeverity.Error ? "Errore" : "Avviso";

    public static string BadgeClass(IssueSeverity severity) => severity == IssueSeverity.Error ? "text-bg-danger" : "text-bg-warning";

    public static string Label(ProposalStatus status) => status switch
    {
        ProposalStatus.Pending => "In attesa",
        ProposalStatus.Accepted => "Accettata",
        ProposalStatus.Rejected => "Rifiutata",
        _ => status.ToString()
    };

    public static string BadgeClass(ProposalStatus status) => status switch
    {
        ProposalStatus.Accepted => "text-bg-success",
        ProposalStatus.Rejected => "text-bg-secondary",
        _ => "text-bg-info"
    };

    public static string Label(ProposalSource source) => source switch
    {
        ProposalSource.TmsOrder => "Ordine del TMS",
        ProposalSource.Document => "Documento",
        ProposalSource.Inference => "Deduzione",
        _ => source.ToString()
    };

    public static string Label(ExtractionFieldOutcome outcome) => outcome switch
    {
        ExtractionFieldOutcome.Matches => "Coincide",
        ExtractionFieldOutcome.Proposed => "Proposta",
        ExtractionFieldOutcome.InReview => "In revisione",
        ExtractionFieldOutcome.NotApplicable => "Non applicabile",
        _ => outcome.ToString()
    };

    public static string BadgeClass(ExtractionFieldOutcome outcome) => outcome switch
    {
        ExtractionFieldOutcome.Matches => "text-bg-success",
        ExtractionFieldOutcome.Proposed => "text-bg-info",
        ExtractionFieldOutcome.InReview => "text-bg-warning",
        _ => "text-bg-secondary"
    };

    public static string Label(ReviewStatus status) => status switch
    {
        ReviewStatus.Pending => "In attesa",
        ReviewStatus.Confirmed => "Confermata",
        ReviewStatus.Rejected => "Rifiutata",
        _ => status.ToString()
    };

    public static string Label(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Success => "Riuscita",
        AuditOutcome.Denied => "Rifiutata",
        AuditOutcome.Failed => "Fallita",
        _ => outcome.ToString()
    };

    public static string BadgeClass(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Success => "text-bg-success",
        AuditOutcome.Denied => "text-bg-danger",
        _ => "text-bg-warning"
    };

    /// <summary>Un segnale della confidenza: vero, falso o non applicabile.</summary>
    public static string Signal(bool? signal) => signal switch
    {
        true => "✓",
        false => "✗",
        null => "–"
    };

    public static string SignalClass(bool? signal) => signal switch
    {
        true => "text-success",
        false => "text-danger fw-bold",
        null => "text-muted"
    };

    /// <summary>La confidenza rispetto alla soglia: sotto soglia è ciò che va guardato.</summary>
    public static string ConfidenceClass(decimal confidence, decimal threshold) => confidence >= threshold ? "text-bg-success" : "text-bg-warning";

    public static string Number(decimal value, string format = "0.00") => value.ToString(format, Italian);

    public static string Kilograms(decimal? value) => value is { } kilograms ? $"{kilograms.ToString("#,##0.###", Italian)} kg" : "—";

    public static string Day(DateOnly? day) => day?.ToString("dd/MM/yyyy", Italian) ?? "—";

    public static string Moment(DateTimeOffset? moment) => moment?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", Italian) ?? "—";

    public static string Seconds(double milliseconds) => $"{(milliseconds / 1000).ToString("0.0", Italian)} s";

    public static string Json(JsonElement element) => JsonSerializer.Serialize(element, Indented);

    /// <summary>Indirizzo della traccia nel dashboard di Aspire; <c>null</c> se il dashboard non è noto o manca il trace id.</summary>
    public static string? TraceUrl(string? dashboardUrl, string? traceId) =>
        string.IsNullOrWhiteSpace(dashboardUrl) || string.IsNullOrWhiteSpace(traceId) ? null : $"{dashboardUrl.TrimEnd('/')}/traces/detail/{traceId}";
}

/// <summary>
/// Indirizzo del dashboard di Aspire, passato dall'AppHost (<c>TRASPORTO_DASHBOARD_URL</c>): serve al link "apri la traccia".
/// Senza valore (Web avviata da sola, test) il link non compare.
/// </summary>
public sealed class DashboardOptions
{
    public const string Key = "TRASPORTO_DASHBOARD_URL";

    [ConfigurationKeyName(Key)]
    public string? Url { get; set; }
}
