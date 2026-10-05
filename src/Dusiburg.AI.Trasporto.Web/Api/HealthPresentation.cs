using Dusiburg.AI.Trasporto.Shared.Health;

namespace Dusiburg.AI.Trasporto.Web.Api;

/// <summary>Come si mostra l'esito di un controllo: etichetta e classe del badge.</summary>
public static class HealthPresentation
{
    public static string Label(ProbeStatus status) => status switch
    {
        ProbeStatus.Ok => "OK",
        ProbeStatus.Info => "Info",
        ProbeStatus.Warning => "Avviso",
        _ => "Errore"
    };

    public static string BadgeClass(ProbeStatus status) => status switch
    {
        ProbeStatus.Ok => "text-bg-success",
        ProbeStatus.Info => "text-bg-secondary",
        ProbeStatus.Warning => "text-bg-warning",
        _ => "text-bg-danger"
    };
}
