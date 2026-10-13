using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Dusiburg.AI.Trasporto.Mcp.Hosting;

/// <summary>
/// Filtro su ogni <c>tools/call</c>: span <c>mcp.tool {tool.name}</c> con <c>tool.name</c>, <c>actor.role</c> e
/// <c>tool.outcome</c>, log strutturato della chiamata e traduzione delle eccezioni negli errori strutturati. Senza filtro l'SDK
/// trasformerebbe ogni eccezione in un testo generico: qui nessuna eccezione arriva al client.
/// </summary>
internal static class ToolCallFilter
{
    public const string LoggerCategory = "Dusiburg.AI.Trasporto.Mcp.ToolCalls";

    private const string InternalErrorMessage = "Errore interno durante l'esecuzione del tool.";

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create(ActivitySource activitySource) =>
        next => async (context, cancellationToken) =>
        {
            string toolName = context.Params?.Name ?? "unknown";
            HttpContext? http = context.Services?.GetService<IHttpContextAccessor>()?.HttpContext;
            ILogger logger = context.Services?.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;
            long started = Stopwatch.GetTimestamp();

            using Activity? activity = activitySource.StartActivity($"mcp.tool {toolName}");
            activity?.SetTag(TrasportoTelemetry.Attributes.ToolName, toolName);
            activity?.SetTag(TrasportoTelemetry.Attributes.ActorRole, http is null ? null : TrasportoMcpServerExtensions.ActorOf(http).Role.ToString());

            CallToolResult result;
            string? errorCode = null;

            try
            {
                result = await next(context, cancellationToken);
            }
            catch (ToolException exception)
            {
                errorCode = exception.Code;
                result = ToolResults.Error(exception.Code, exception.Message);
            }
            catch (McpProtocolException)
            {
                // Tool inesistente e altri errori di protocollo: restano errori JSON-RPC.
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException)
            {
                // Argomenti mancanti o non convertibili (binding dell'SDK).
                errorCode = ErrorCodes.ValidationError;
                result = ToolResults.Error(errorCode, exception.Message);
            }
            catch (Exception exception)
            {
                errorCode = ErrorCodes.Internal;
                result = ToolResults.Error(errorCode, InternalErrorMessage);

                logger.LogError(exception, "Eccezione non gestita nel tool {ToolName}", toolName);
            }

            string outcome = errorCode is null ? "ok" : $"error:{errorCode}";

            activity?.SetTag(TrasportoTelemetry.Attributes.ToolOutcome, outcome);

            if (errorCode is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, errorCode);
            }

            logger.Log(
                errorCode is null ? LogLevel.Information : LogLevel.Warning,
                "Tool {ToolName} completato con esito {ToolOutcome} in {ElapsedMs} ms",
                toolName,
                outcome,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return result;
        };
}
