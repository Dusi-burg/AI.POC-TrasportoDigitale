using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dusiburg.AI.Trasporto.Agents.Authority;
using Dusiburg.AI.Trasporto.Agents.Mapping;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using ModelContextProtocol;

namespace Dusiburg.AI.Trasporto.Agents.Endpoints;

/// <summary>
/// Avvio degli agenti: risposta unica oppure stream SSE con un evento per passo (come la pre-istruttoria di Sinistri). Chi chiama
/// porta la propria chiave di ruolo, e l'agente la usa per i tool: qui si controlla solo che la chiave sia di qualcuno.
/// </summary>
internal static class AgentEndpoints
{
    private const int MaxQuestionLength = 500;

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder agents = app.MapGroup("/api/agents");

        agents.MapPost("/mapping/run", RunMappingAsync);
        agents.MapPost("/mapping/run/stream", StreamMapping);
        agents.MapPost("/authority/ask", AskAuthorityAsync);
        agents.MapPost("/authority/ask/stream", StreamAuthority);

        return app;
    }

    private static async Task<Results<Ok<MappingRunResult>, ProblemHttpResult>> RunMappingAsync(
        MappingRunRequest request, MappingAgent agent, ActorResolver resolver, HttpContext http, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (Refuse(http, resolver, out string apiKey) is { } refusal)
        {
            return refusal;
        }

        if (ShipmentCodeOf(request) is not { } shipmentCode)
        {
            return ApiProblems.Validation("Indicare la spedizione (shipmentCode).");
        }

        try
        {
            return TypedResults.Ok(await agent.RunAsync(shipmentCode, apiKey, progress: null, cancellationToken));
        }
        catch (Exception exception) when (ToProblem(exception, Logger(loggerFactory), MappingAgent.AgentName) is { } problem)
        {
            return problem;
        }
    }

    private static Results<ServerSentEventsResult<object>, ProblemHttpResult> StreamMapping(
        MappingRunRequest request, MappingAgent agent, ActorResolver resolver, HttpContext http, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (Refuse(http, resolver, out string apiKey) is { } refusal)
        {
            return refusal;
        }

        if (ShipmentCodeOf(request) is not { } shipmentCode)
        {
            return ApiProblems.Validation("Indicare la spedizione (shipmentCode).");
        }

        return TypedResults.ServerSentEvents(Events(
            MappingAgent.AgentName, Logger(loggerFactory), async progress => await agent.RunAsync(shipmentCode, apiKey, progress, cancellationToken), cancellationToken));
    }

    private static async Task<Results<Ok<AuthorityAnswerResult>, ProblemHttpResult>> AskAuthorityAsync(
        AuthorityAskRequest request, AuthorityAgent agent, ActorResolver resolver, HttpContext http, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (Refuse(http, resolver, out string apiKey) is { } refusal)
        {
            return refusal;
        }

        if (Validate(request, out string plate, out string question) is { } invalid)
        {
            return invalid;
        }

        try
        {
            return TypedResults.Ok(await agent.AskAsync(plate, question, apiKey, progress: null, cancellationToken));
        }
        catch (Exception exception) when (ToProblem(exception, Logger(loggerFactory), AuthorityAgent.AgentName) is { } problem)
        {
            return problem;
        }
    }

    private static Results<ServerSentEventsResult<object>, ProblemHttpResult> StreamAuthority(
        AuthorityAskRequest request, AuthorityAgent agent, ActorResolver resolver, HttpContext http, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (Refuse(http, resolver, out string apiKey) is { } refusal)
        {
            return refusal;
        }

        if (Validate(request, out string plate, out string question) is { } invalid)
        {
            return invalid;
        }

        return TypedResults.ServerSentEvents(Events(
            AuthorityAgent.AgentName, Logger(loggerFactory), async progress => await agent.AskAsync(plate, question, apiKey, progress, cancellationToken), cancellationToken));
    }

    /// <summary>
    /// <c>text/event-stream</c>: eventi <c>step</c>, poi <c>result</c> oppure <c>error</c>. Una volta partito lo stream il codice
    /// HTTP è già 200: gli errori viaggiano come evento, con lo stesso ProblemDetails della risposta unica.
    /// </summary>
    internal static async IAsyncEnumerable<SseItem<object>> Events(
        string agentName, ILogger logger, Func<IProgress<AgentStep>, Task<object>> run, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<SseItem<object>> channel = Channel.CreateUnbounded<SseItem<object>>(new UnboundedChannelOptions { SingleReader = true });

        Task execution = Task.Run(async () =>
        {
            try
            {
                object result = await run(new ChannelProgress(channel.Writer));

                channel.Writer.TryWrite(new SseItem<object>(result, AgentEvents.Result));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ProblemHttpResult problem = ToProblem(exception, logger, agentName) ?? Unexpected(exception, logger, agentName);

                channel.Writer.TryWrite(new SseItem<object>(problem.ProblemDetails, AgentEvents.Error));
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, CancellationToken.None);

        await foreach (SseItem<object> item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }

        await execution;
    }

    /// <summary>Senza una chiave riconosciuta non si avvia nessun agente: non avrebbe un ruolo con cui chiamare i tool.</summary>
    private static ProblemHttpResult? Refuse(HttpContext http, ActorResolver resolver, out string apiKey)
    {
        apiKey = ((string?)http.Request.Headers[ActorResolver.ApiKeyHeaderName])?.Trim() ?? string.Empty;

        Actor? actor = resolver.Resolve(apiKey);

        return actor is null
            ? ApiProblems.Unauthorized($"Intestazione {ActorResolver.ApiKeyHeaderName} assente o non riconosciuta.")
            : null;
    }

    private static string? ShipmentCodeOf(MappingRunRequest request) =>
        string.IsNullOrWhiteSpace(request.ShipmentCode) ? null : request.ShipmentCode.Trim();

    private static ProblemHttpResult? Validate(AuthorityAskRequest request, out string plate, out string question)
    {
        plate = request.Plate?.Trim() ?? string.Empty;
        question = request.Question?.Trim() ?? string.Empty;

        if (plate.Length == 0 || question.Length == 0)
        {
            return ApiProblems.Validation("Indicare la targa (plate) e la domanda (question).");
        }

        return question.Length > MaxQuestionLength
            ? ApiProblems.Validation($"La domanda può avere al massimo {MaxQuestionLength} caratteri.")
            : null;
    }

    /// <summary>
    /// Gli errori noti di un run: ruolo senza i tool dell'agente, server MCP o modello non raggiungibili, modello non
    /// configurato. <c>null</c> per tutto il resto, che resta un errore non gestito.
    /// </summary>
    private static ProblemHttpResult? ToProblem(Exception exception, ILogger logger, string agentName)
    {
        switch (exception)
        {
            case AgentUnavailableException:
                return ApiProblems.Forbidden(exception.Message);

            case HttpRequestException or McpException or TaskCanceledException { InnerException: TimeoutException }:
                logger.LogError(exception, "{Agent}: un servizio a valle non risponde", agentName);

                return ApiProblems.UpstreamUnavailable("Il server dei tool o il modello non risponde: riprovare fra poco.");

            case InvalidOperationException when exception.Message.Contains("ANTHROPIC_API_KEY", StringComparison.Ordinal):
                return ApiProblems.UpstreamUnavailable("Il modello cloud non è configurato: manca la chiave Anthropic.");

            default:
                return null;
        }
    }

    private static ProblemHttpResult Unexpected(Exception exception, ILogger logger, string agentName)
    {
        logger.LogError(exception, "{Agent}: run non completato", agentName);

        return TypedResults.Problem(detail: "Il run dell'agente non è stato completato.", statusCode: StatusCodes.Status500InternalServerError, title: "Errore interno");
    }

    private static ILogger Logger(ILoggerFactory loggerFactory) => loggerFactory.CreateLogger(typeof(AgentEndpoints));

    /// <summary>
    /// Scrive i passi nel canale nel thread dell'agente: <see cref="Progress{T}"/> li pubblicherebbe sul thread pool, senza
    /// garanzia d'ordine rispetto all'evento finale.
    /// </summary>
    private sealed class ChannelProgress(ChannelWriter<SseItem<object>> writer) : IProgress<AgentStep>
    {
        public void Report(AgentStep value) => writer.TryWrite(new SseItem<object>(value, AgentEvents.Step));
    }
}
