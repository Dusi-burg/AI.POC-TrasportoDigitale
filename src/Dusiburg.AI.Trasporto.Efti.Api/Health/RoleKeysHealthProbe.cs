using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Efti.Api.Health;

/// <summary>
/// Controllo 12 di <c>/api/health</c>: ogni ruolo ha la sua chiave. Un ruolo senza chiave non può autenticarsi: la piattaforma
/// funziona, ma quella parte della demo no. Dice solo quali ruoli sono configurati, mai le chiavi.
/// </summary>
public sealed class RoleKeysHealthProbe(ActorResolver resolver) : IHealthProbe
{
    public const int Number = 12;

    public const string Name = "Chiavi di ruolo";

    public Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken)
    {
        Role[] missing = [.. Enum.GetValues<Role>().Except(resolver.ConfiguredRoles)];

        ProbeResult result = missing.Length == 0
            ? new ProbeResult(Number, Name, ProbeStatus.Ok, $"configurate per {string.Join(", ", resolver.ConfiguredRoles)}", TimeSpan.Zero)
            : new ProbeResult(Number, Name, ProbeStatus.Warning,
                $"mancano le chiavi di {string.Join(", ", missing)}: impostare Parameters:api-key-<ruolo> negli user-secrets dell'AppHost", TimeSpan.Zero);

        return Task.FromResult<IReadOnlyList<ProbeResult>>([result]);
    }
}
