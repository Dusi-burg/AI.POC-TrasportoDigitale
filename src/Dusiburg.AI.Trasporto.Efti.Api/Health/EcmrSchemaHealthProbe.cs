using Dusiburg.AI.Trasporto.Efti.Core.Ecmr;
using Dusiburg.AI.Trasporto.Shared.Health;

namespace Dusiburg.AI.Trasporto.Efti.Api.Health;

/// <summary>
/// Controllo 13 di <c>/api/health</c>: gli schemi XSD dell'eCMR UN/CEFACT sono accanto all'assembly, si leggono e si compilano.
/// Senza, importazione ed esportazione fallirebbero alla prima richiesta.
/// </summary>
public sealed class EcmrSchemaHealthProbe(IServiceProvider services) : IHealthProbe
{
    public const int Number = 13;

    public const string Name = "Schema eCMR";

    public async Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken) =>
    [
        await ProbeResult.MeasureAsync(Number, Name, () =>
        {
            // Risolto qui e non nel costruttore: se lo schema manca, l'errore diventa l'esito del controllo.
            EcmrSchema schema = services.GetRequiredService<EcmrSchema>();

            return Task.FromResult((ProbeStatus.Ok, $"{EcmrSchema.RootSchemaFile} versione {schema.Version}, {schema.SchemaCount} schemi"));
        })
    ];
}
