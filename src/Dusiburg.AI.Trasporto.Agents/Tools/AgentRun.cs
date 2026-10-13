using System.Diagnostics;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Telemetry;

namespace Dusiburg.AI.Trasporto.Agents.Tools;

/// <summary>
/// Ciò che accade in un run di un agente, registrato dal codice e non dal modello: chiamate ai tool con il loro esito, avvisi,
/// passi da mostrare mentre accadono. I fatti di dominio (proposte, sottoinsiemi letti) li tiene l'agente.
/// </summary>
public sealed class AgentRun(string agentName, IReadOnlySet<string> allowedTools, IProgress<AgentStep>? progress)
{
    public static readonly ActivitySource Source = new(TrasportoTelemetry.Sources.Agents);

    private readonly List<AgentToolCall> _toolCalls = [];
    private readonly List<string> _warnings = [];

    public string AgentName { get; } = agentName;

    public IReadOnlySet<string> AllowedTools { get; } = allowedTools;

    public IReadOnlyList<AgentToolCall> ToolCalls => _toolCalls;

    public IReadOnlyList<string> Warnings => _warnings;

    public void RecordCall(string tool, string outcome, double elapsedMs, string message)
    {
        _toolCalls.Add(new AgentToolCall(tool, outcome, elapsedMs));

        Report(new AgentStep(AgentStepKinds.Tool, tool, outcome, message, elapsedMs));
    }

    public void Warn(string warning)
    {
        if (!_warnings.Contains(warning))
        {
            _warnings.Add(warning);
        }
    }

    public void Report(AgentStep step) => progress?.Report(step);
}
