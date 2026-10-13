using System.Security.Cryptography;
using System.Text;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.Extensions.Configuration;

namespace Dusiburg.AI.Trasporto.ServiceDefaults.Security;

/// <summary>
/// Identità simulata (G12): la API key della richiesta dice chi sta chiamando. Una chiave per ruolo, nella sezione
/// <c>ApiKeys</c> della configurazione (<c>ApiKeys__Dispatcher</c>, … passate dall'AppHost). Un ruolo senza chiave non può
/// autenticarsi. La usano allo stesso modo <c>Efti.Api</c> e i server MCP.
/// </summary>
public sealed class ActorResolver
{
    public const string ApiKeyHeaderName = "X-Api-Key";

    public const string ConfigurationSection = "ApiKeys";

    private readonly (byte[] Key, Actor Actor)[] _keys;

    public ActorResolver(IConfiguration configuration)
    {
        _keys =
        [
            .. Enum.GetValues<Role>()
                .Select(role => (Key: configuration[$"{ConfigurationSection}:{role}"], Actor: DemoActors.For(role)))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .Select(entry => (Encoding.UTF8.GetBytes(entry.Key!.Trim()), entry.Actor))
        ];
    }

    /// <summary>Ruoli che hanno una chiave configurata.</summary>
    public IReadOnlyList<Role> ConfiguredRoles => [.. _keys.Select(k => k.Actor.Role)];

    /// <summary>L'attore a cui appartiene la chiave; <c>null</c> se manca o non è di nessuno.</summary>
    public Actor? Resolve(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        byte[] candidate = Encoding.UTF8.GetBytes(apiKey.Trim());
        Actor? found = null;

        // Si confrontano tutte le chiavi, a tempo costante: la durata non rivela né quale ruolo né quanti caratteri coincidono.
        foreach ((byte[] key, Actor actor) in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(candidate, key))
            {
                found = actor;
            }
        }

        return found;
    }
}
