using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Agents;

// Contratti del servizio Agents (PLAN §5, Fase 6). L'esito di un run viene dai risultati dei tool: ciò che il modello racconta
// (sintesi, risposta) è testo da mostrare, non un fatto.

/// <summary>Nomi degli eventi dello stream SSE di un agente.</summary>
public static class AgentEvents
{
    public const string Step = "step";
    public const string Result = "result";
    public const string Error = "error";
}

public static class AgentStepKinds
{
    /// <summary>Il modello sta lavorando.</summary>
    public const string Model = "model";

    /// <summary>Una chiamata a un tool, conclusa.</summary>
    public const string Tool = "tool";
}

/// <summary>Un passo del run, per mostrarlo mentre accade.</summary>
/// <param name="Outcome"><c>ok</c> oppure <c>error:&lt;codice&gt;</c>; <c>null</c> per i passi del modello.</param>
public sealed record AgentStep(string Kind, string? Tool, string? Outcome, string Message, double? ElapsedMs);

/// <param name="Outcome"><c>ok</c> oppure <c>error:&lt;codice&gt;</c>.</param>
public sealed record AgentToolCall(string Tool, string Outcome, double ElapsedMs);

/// <summary>Chi ha risposto e quanto è costato: serve al confronto fra locale e cloud.</summary>
public sealed record AgentModelInfo(string Provider, string Model, long? InputTokens, long? OutputTokens, double ElapsedMs);

public sealed record MappingRunRequest(string? ShipmentCode);

/// <param name="Created"><c>false</c> se la stessa proposta era già in attesa.</param>
public sealed record MappingProposal(int ProposalId, string FieldPath, string ProposedValue, string? CurrentValue, bool Created);

/// <summary>Una proposta che il modello ha tentato e che non è stata registrata.</summary>
/// <param name="Reason">Perché: valore non riconducibile alla fonte, campo non completabile, e così via.</param>
public sealed record DiscardedProposal(string? FieldPath, string? Value, string Reason);

/// <param name="DatasetStatus">Stato del dataset letto dall'agente; <c>null</c> se non ha letto i problemi.</param>
/// <param name="Summary">Sintesi scritta dal modello: testo da mostrare, non un fatto.</param>
public sealed record MappingRunResult(
    string ShipmentCode,
    DatasetStatus? DatasetStatus,
    IReadOnlyList<MappingProposal> Proposals,
    IReadOnlyList<DiscardedProposal> Discarded,
    string? Summary,
    IReadOnlyList<AgentToolCall> ToolCalls,
    AgentModelInfo Model,
    IReadOnlyList<string> Warnings);

/// <param name="At">Momento del controllo, se simulato (demo); <c>null</c> = adesso.</param>
public sealed record AuthorityAskRequest(string? Plate, string? Question, DateTimeOffset? At = null);

public sealed record SubsetReading(string Uil, string SubsetId);

/// <param name="Answer">Risposta scritta dal modello.</param>
/// <param name="DataAvailable">Il modello dichiara di aver trovato il dato nei sottoinsiemi letti.</param>
/// <param name="QuotedValues">Valori che la risposta cita come presi dai dati.</param>
/// <param name="UnsupportedValues">Fra i valori citati, quelli che non compaiono nei dati letti: la risposta va presa con riserva.</param>
/// <param name="Uils">UIL delle spedizioni a bordo del mezzo al momento del controllo.</param>
/// <param name="ControlMoment">Momento del controllo, se simulato; <c>null</c> = adesso.</param>
public sealed record AuthorityAnswerResult(
    string Plate,
    string Question,
    string Answer,
    bool DataAvailable,
    IReadOnlyList<string> QuotedValues,
    IReadOnlyList<string> UnsupportedValues,
    IReadOnlyList<string> Uils,
    IReadOnlyList<SubsetReading> SubsetsRead,
    IReadOnlyList<AgentToolCall> ToolCalls,
    AgentModelInfo Model,
    IReadOnlyList<string> Warnings,
    DateTimeOffset? ControlMoment = null);
