using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Shared.Health;

namespace Dusiburg.AI.Trasporto.Efti.Api.Health;

/// <summary>
/// Controllo 11 di <c>/api/health</c>: lo schema XSD eFTI è accanto all'assembly, si legge e si compila. Senza, importazione
/// ed esportazione fallirebbero alla prima richiesta.
/// </summary>
public sealed class EftiSchemaHealthProbe(IServiceProvider services) : IHealthProbe
{
    public const int Number = 11;

    public const string Name = "Schema eFTI";

    public async Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken) =>
    [
        await ProbeResult.MeasureAsync(Number, Name, () =>
        {
            // Risolti qui e non nel costruttore: se lo schema manca, l'errore diventa l'esito del controllo.
            EftiSchemaCatalog catalog = services.GetRequiredService<EftiSchemaCatalog>();
            services.GetRequiredService<EftiXmlValidator>();

            return Task.FromResult((ProbeStatus.Ok, $"{EftiSchemaCatalog.CommonSchemaFile} versione {catalog.Version}, {catalog.ComplexTypeNames.Count()} tipi"));
        })
    ];
}
