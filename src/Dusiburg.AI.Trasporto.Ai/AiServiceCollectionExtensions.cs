using Dusiburg.AI.Trasporto.Ai.Health;
using Dusiburg.AI.Trasporto.Ai.Ollama;
using Dusiburg.AI.Trasporto.Shared.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dusiburg.AI.Trasporto.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Manopole dei modelli, fabbriche dei client e probe di Ollama e della chiave Anthropic. I client non si registrano:
    /// chi li usa li chiede alla fabbrica per il proprio compito.
    /// </summary>
    public static IServiceCollection AddTrasportoAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => AiOptions.FromConfiguration(configuration));
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<EmbeddingGeneratorFactory>();
        services.AddSingleton<IHealthProbe, OllamaHealthProbe>();
        services.AddSingleton<IHealthProbe, AnthropicKeyHealthProbe>();

        return services;
    }
}
