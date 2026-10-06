using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Shared.Telemetry;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Agents.Tools;

/// <summary>Un tool rifiutato dal codice prima di arrivare al server: il modello riceve lo stesso involucro d'errore dei tool.</summary>
public sealed record ToolRejection(string Code, string Message);

/// <summary>
/// Ciò che il codice impone a un tool di un agente: quali argomenti il modello non vede perché li mette il codice, che cosa
/// controllare prima della chiamata, che cosa annotare dopo.
/// </summary>
public sealed class ToolRule
{
    /// <summary>Argomenti tolti dallo schema offerto al modello: li inietta <see cref="Before"/>.</summary>
    public IReadOnlyList<string> HiddenArguments { get; init; } = [];

    /// <summary>Prima della chiamata: inietta gli argomenti del codice e può rifiutare.</summary>
    public Func<AIFunctionArguments, ToolRejection?>? Before { get; init; }

    public Action<AIFunctionArguments, JsonElement>? OnSuccess { get; init; }

    public Action<AIFunctionArguments, string, string>? OnError { get; init; }
}

/// <summary>
/// Guardia attorno a ogni tool offerto a un agente: allow-list, regole del codice, registrazione della chiamata nel run e span
/// <c>agent.tool</c>. L'agente non ha altro modo di agire.
/// </summary>
public sealed class GuardedTool : DelegatingAIFunction
{
    private readonly AgentRun _run;
    private readonly ToolRule _rule;
    private readonly JsonElement _schema;

    public GuardedTool(AIFunction tool, AgentRun run, ToolRule? rule = null)
        : base(tool)
    {
        _run = run;
        _rule = rule ?? new ToolRule();
        _schema = _rule.HiddenArguments.Count == 0 ? tool.JsonSchema : WithoutProperties(tool.JsonSchema, _rule.HiddenArguments);
    }

    public override JsonElement JsonSchema => _schema;

    /// <summary>Il testo di un argomento, qualunque forma gli abbia dato il modello (stringa, numero, JSON).</summary>
    public static string? ArgumentText(AIFunctionArguments arguments, string name) =>
        arguments.TryGetValue(name, out object? value) ? value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement json => json.GetRawText(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        } : null;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        using Activity? activity = AgentRun.Source.StartActivity($"agent.tool {Name}");
        activity?.SetTag(TrasportoTelemetry.Attributes.AgentName, _run.AgentName);
        activity?.SetTag(TrasportoTelemetry.Attributes.ToolName, Name);

        long started = Stopwatch.GetTimestamp();
        string outcome = $"error:{ErrorCodes.Internal}";
        string message = string.Empty;

        try
        {
            (ToolResult result, object? returnValue) = await InvokeGuardedAsync(arguments, cancellationToken);

            outcome = result.Succeeded ? "ok" : $"error:{result.ErrorCode}";
            message = result.Succeeded ? string.Empty : result.ErrorMessage ?? string.Empty;

            return returnValue;
        }
        finally
        {
            activity?.SetTag(TrasportoTelemetry.Attributes.ToolOutcome, outcome);

            if (outcome != "ok")
            {
                activity?.SetStatus(ActivityStatusCode.Error, outcome);
            }

            _run.RecordCall(Name, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds, message);
        }
    }

    private async Task<(ToolResult Result, object? ReturnValue)> InvokeGuardedAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // Difesa in profondità: all'agente si offrono solo i tool consentiti, ma la guardia non si fida.
        if (!_run.AllowedTools.Contains(Name))
        {
            return Reject(arguments, new ToolRejection(ErrorCodes.Unauthorized, $"Tool '{Name}' is not allowed for agent {_run.AgentName}."));
        }

        if (_rule.Before?.Invoke(arguments) is { } rejection)
        {
            return Reject(arguments, rejection);
        }

        object? value = await base.InvokeCoreAsync(arguments, cancellationToken);
        ToolResult result = ToolResultReader.Read(value);

        if (result.Succeeded)
        {
            _rule.OnSuccess?.Invoke(arguments, result.Data);
        }
        else
        {
            _rule.OnError?.Invoke(arguments, result.ErrorCode ?? ErrorCodes.Internal, result.ErrorMessage ?? string.Empty);
        }

        return (result, value);
    }

    private (ToolResult Result, object? ReturnValue) Reject(AIFunctionArguments arguments, ToolRejection rejection)
    {
        _rule.OnError?.Invoke(arguments, rejection.Code, rejection.Message);

        JsonElement envelope = JsonSerializer.SerializeToElement(ToolErrorResponse.Create(rejection.Code, rejection.Message), TrasportoJson.Options);

        return (new ToolResult(false, envelope, rejection.Code, rejection.Message), envelope);
    }

    private static JsonElement WithoutProperties(JsonElement schema, IReadOnlyList<string> names)
    {
        JsonObject node = JsonNode.Parse(schema.GetRawText())?.AsObject() ?? [];

        if (node["properties"] is JsonObject properties)
        {
            foreach (string name in names)
            {
                properties.Remove(name);
            }
        }

        if (node["required"] is JsonArray required)
        {
            foreach (JsonNode? item in required.Where(i => i is not null && names.Contains(i.GetValue<string>())).ToList())
            {
                required.Remove(item);
            }
        }

        return JsonSerializer.SerializeToElement(node);
    }
}
