using System.Diagnostics;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Agents.Tools;

/// <summary>
/// Ciò che i due agenti hanno in comune nel parlare con il modello: opzioni di default del provider più istruzioni e tool,
/// conteggio dei token, riconoscimento del rifiuto.
/// </summary>
public sealed class AgentModel(ChatModel model, AgentRun run)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _inputTokens;
    private long _outputTokens;
    private bool _usageKnown;

    public ChatClientAgent CreateAgent(string instructions, IList<AITool> tools)
    {
        ChatOptions options = model.DefaultOptions.Clone();

        options.Instructions = instructions;
        options.Tools = tools;

        return new ChatClientAgent(model.ChatClient, new ChatClientAgentOptions { Name = run.AgentName, ChatOptions = options });
    }

    /// <summary>
    /// Annota i token della risposta e dice se il modello si è rifiutato di rispondere. Un rifiuto non si ritenta: il run si
    /// chiude con un avviso e con i fatti raccolti fino a lì.
    /// </summary>
    public bool Refused(AgentResponse response)
    {
        if (response.Usage is { } usage)
        {
            _usageKnown = true;
            _inputTokens += usage.InputTokenCount ?? 0;
            _outputTokens += usage.OutputTokenCount ?? 0;
        }

        if (response.FinishReason != ChatFinishReason.ContentFilter)
        {
            return false;
        }

        run.Warn("Il modello ha rifiutato di rispondere: il run si è fermato senza nuovi tentativi.");

        return true;
    }

    /// <summary>
    /// Il testo conclusivo del modello: l'ultimo messaggio, non tutto ciò che ha scritto fra una chiamata a un tool e l'altra.
    /// </summary>
    public static string? FinalText(AgentResponse response)
    {
        string? text = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))?.Text.Trim();

        return string.IsNullOrEmpty(text) ? null : text;
    }

    public AgentModelInfo Info() => new(
        model.Provider, model.ModelId, _usageKnown ? _inputTokens : null, _usageKnown ? _outputTokens : null,
        Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
}
