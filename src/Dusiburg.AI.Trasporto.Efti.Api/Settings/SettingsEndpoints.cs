using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Efti.Core.Gate;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.Extensions.Options;

namespace Dusiburg.AI.Trasporto.Efti.Api.Settings;

/// <summary>
/// La configurazione con cui il servizio sta lavorando, per la pagina di stato della UI: quale modello risponde per ogni
/// compito, pesi e soglia dell'estrazione, permanenza sul gate. Sola lettura e nessun segreto: le manopole si cambiano
/// sull'AppHost, non da qui.
/// </summary>
internal static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", (AiOptions ai, IOptions<ExtractionOptions> extraction, IOptions<EftiPlatformOptions> platform) =>
            {
                ExtractionOptions options = extraction.Value;

                return TypedResults.Ok(new SettingsView(
                    [.. Enum.GetValues<ModelTask>().Select(task => ToView(task, ai))],
                    new ExtractionSettingsView(
                        options.Weights.Literal, options.Weights.Format, options.Weights.Tms, options.Weights.Agreement, options.Weights.Annotation,
                        options.ReviewThreshold, options.Passes, options.AutoAccept),
                    platform.Value.GateRetentionMinutes));
            })
            .RequireOperation(Operation.SettingsRead);

        return api;
    }

    private static TaskModelView ToView(ModelTask task, AiOptions ai)
    {
        string provider = ai.ProviderFor(task);

        return new TaskModelView(task.ToString(), provider, provider == ModelProviders.Anthropic ? ai.AnthropicModelFor(task) : ai.OllamaChatModel);
    }
}
