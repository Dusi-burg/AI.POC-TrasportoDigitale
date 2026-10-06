using System.Collections.Frozen;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Agents.Grounding;
using Dusiburg.AI.Trasporto.Agents.Specs;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tools;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Agents.Mapping;

/// <summary>
/// Agente di mappatura: legge i problemi di una spedizione e il suo ordine nel TMS e lascia proposte di completamento. Non
/// modifica nulla e non decide nulla. Spedizione e fonte le fissa il codice; una proposta parte solo se il valore si ritrova
/// nell'ordine letto in questo run; l'esito sono i risultati dei tool, non il racconto del modello.
/// </summary>
public sealed class MappingAgent(IChatClientFactory models, IAgentToolCatalog catalog, ILogger<MappingAgent> logger)
{
    public const string AgentName = "MappingAgent";

    public const string TaskSection = "Task";

    private static readonly AgentSpec Spec = AgentSpec.Load(AgentName);

    public static IReadOnlySet<string> AllowedTools { get; } = new[]
    {
        EftiToolNames.ListValidationIssues,
        EftiToolNames.GetTmsOrder,
        EftiToolNames.ProposeFieldCompletion
    }.ToFrozenSet();

    /// <summary>Argomenti di <c>propose_field_completion</c> che il modello non vede: li mette il codice.</summary>
    public static readonly IReadOnlyList<string> InjectedProposalArguments = ["shipmentCode", "source"];

    public async Task<MappingRunResult> RunAsync(string shipmentCode, string apiKey, IProgress<AgentStep>? progress, CancellationToken cancellationToken)
    {
        var run = new AgentRun(AgentName, AllowedTools, progress);
        var facts = new MappingFacts(shipmentCode);

        await using IAgentToolSession session = await catalog.OpenAsync(apiKey, cancellationToken);
        List<AITool> tools = session.Guarded(run, Rules(facts));

        using ChatModel chatModel = models.Create(ModelTask.Mapping);
        var model = new AgentModel(chatModel, run);
        ChatClientAgent agent = model.CreateAgent(Spec.Instructions, tools);
        AgentSession conversation = await agent.CreateSessionAsync(cancellationToken);

        run.Report(new AgentStep(AgentStepKinds.Model, null, null, $"{chatModel.Provider} · {chatModel.ModelId}", null));

        AgentResponse response = await agent.RunAsync(
            Spec.Section(TaskSection).Replace("{shipmentCode}", shipmentCode, StringComparison.Ordinal), conversation, cancellationToken: cancellationToken);

        bool refused = model.Refused(response);

        if (run.ToolCalls.Count == 0 && !refused)
        {
            run.Warn("Il modello non ha chiamato nessun tool: nessun dato letto, nessuna proposta.");
        }

        logger.LogInformation(
            "{Agent} su {ShipmentCode}: {Proposals} proposte, {Discarded} scartate, {ToolCalls} chiamate ai tool",
            AgentName, shipmentCode, facts.Proposals.Count, facts.Discarded.Count, run.ToolCalls.Count);

        return new MappingRunResult(
            shipmentCode, facts.DatasetStatus, facts.Proposals, facts.Discarded, refused ? null : AgentModel.FinalText(response),
            run.ToolCalls, model.Info(), run.Warnings);
    }

    private static Dictionary<string, ToolRule> Rules(MappingFacts facts) => new()
    {
        [EftiToolNames.ListValidationIssues] = new ToolRule
        {
            HiddenArguments = ["shipmentCode"],
            Before = arguments => facts.InjectShipment(arguments),
            OnSuccess = (_, data) => facts.DatasetStatus =
                data.TryGetProperty("status", out JsonElement status) && Enum.TryParse(status.GetString(), out DatasetStatus parsed) ? parsed : null
        },
        [EftiToolNames.GetTmsOrder] = new ToolRule
        {
            HiddenArguments = ["shipmentCode"],
            Before = arguments => facts.InjectShipment(arguments),
            OnSuccess = (_, data) => facts.TmsOrder = data.TryGetProperty("order", out JsonElement order) ? order.Clone() : data.Clone()
        },
        [EftiToolNames.ProposeFieldCompletion] = new ToolRule
        {
            HiddenArguments = InjectedProposalArguments,
            Before = arguments => facts.CheckProposal(arguments),
            OnSuccess = (_, data) => facts.RecordProposal(data),
            OnError = (arguments, code, message) => facts.Discard(arguments, code, message)
        }
    };

    /// <summary>I fatti del run: ciò che i tool hanno restituito e ciò che il codice ha lasciato o non ha lasciato passare.</summary>
    private sealed class MappingFacts(string shipmentCode)
    {
        private readonly List<MappingProposal> _proposals = [];
        private readonly List<DiscardedProposal> _discarded = [];

        public DatasetStatus? DatasetStatus { get; set; }

        /// <summary>L'ordine del TMS letto in questo run: l'unica fonte ammessa per una proposta.</summary>
        public JsonElement? TmsOrder { get; set; }

        public IReadOnlyList<MappingProposal> Proposals => _proposals;

        public IReadOnlyList<DiscardedProposal> Discarded => _discarded;

        /// <summary>La spedizione è quella del run, qualunque cosa il modello abbia scritto.</summary>
        public ToolRejection? InjectShipment(AIFunctionArguments arguments)
        {
            arguments["shipmentCode"] = shipmentCode;

            return null;
        }

        public ToolRejection? CheckProposal(AIFunctionArguments arguments)
        {
            InjectShipment(arguments);
            arguments["source"] = nameof(ProposalSource.TmsOrder);

            if (TmsOrder is not { } order)
            {
                return new ToolRejection(ErrorCodes.ValidationError, "Call get_tms_order before proposing a value: the order is the only accepted source.");
            }

            // Il valore si porta a testo così com'è stato scritto: un numero resta il numero proposto.
            string? value = GuardedTool.ArgumentText(arguments, "value");

            arguments["value"] = value;

            if (!SourceGrounding.IsGrounded(value, [order], out string? reason))
            {
                return new ToolRejection(ErrorCodes.ValidationError, $"Proposal refused: {reason} Propose only values written in the TMS order.");
            }

            if (string.IsNullOrWhiteSpace(GuardedTool.ArgumentText(arguments, "sourceReference")))
            {
                arguments["sourceReference"] = order.TryGetProperty("order", out JsonElement summary) && summary.TryGetProperty("code", out JsonElement code)
                    ? code.GetString()
                    : null;
            }

            return null;
        }

        public void RecordProposal(JsonElement data)
        {
            var proposal = new MappingProposal(
                data.GetProperty("proposalId").GetInt32(),
                data.GetProperty("fieldPath").GetString()!,
                data.GetProperty("proposedValue").GetString()!,
                data.TryGetProperty("currentValue", out JsonElement current) ? current.GetString() : null,
                data.TryGetProperty("created", out JsonElement created) && created.ValueKind == JsonValueKind.True);

            if (_proposals.All(p => p.ProposalId != proposal.ProposalId))
            {
                _proposals.Add(proposal);
            }
        }

        public void Discard(AIFunctionArguments arguments, string code, string message) =>
            _discarded.Add(new DiscardedProposal(
                GuardedTool.ArgumentText(arguments, "fieldPath"), GuardedTool.ArgumentText(arguments, "value"), $"{code}: {message}"));
    }
}
