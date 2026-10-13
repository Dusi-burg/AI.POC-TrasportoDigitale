using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Hosting;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;

/// <summary>Chiavi di ruolo dei test: una per ruolo, configurate nell'host di prova come farebbe l'AppHost.</summary>
internal static class TestApiKeys
{
    public static string For(Role role) => $"chiave-di-test-{role.ToString().ToLowerInvariant()}";

    public static IWebHostBuilder UseTestApiKeys(this IWebHostBuilder builder)
    {
        foreach (Role role in Enum.GetValues<Role>())
        {
            builder.UseSetting($"{ActorResolver.ConfigurationSection}:{role}", For(role));
        }

        return builder;
    }

    /// <summary>Tutte le richieste del client porteranno la chiave di quel ruolo.</summary>
    public static HttpClient ActingAs(this HttpClient client, Role role)
    {
        client.DefaultRequestHeaders.Remove(ActorResolver.ApiKeyHeaderName);
        client.DefaultRequestHeaders.Add(ActorResolver.ApiKeyHeaderName, For(role));

        return client;
    }

    /// <summary>Una richiesta con la chiave indicata, qualunque sia quella predefinita del client; <c>null</c> = nessuna chiave.</summary>
    public static HttpRequestMessage WithApiKey(this HttpRequestMessage request, string? apiKey)
    {
        request.Headers.Remove(ActorResolver.ApiKeyHeaderName);

        if (apiKey is not null)
        {
            request.Headers.Add(ActorResolver.ApiKeyHeaderName, apiKey);
        }

        return request;
    }
}
