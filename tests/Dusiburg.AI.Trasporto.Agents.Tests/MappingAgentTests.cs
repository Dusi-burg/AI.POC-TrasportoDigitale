using Dusiburg.AI.Trasporto.Agents.Mapping;
using Dusiburg.AI.Trasporto.Agents.Tests.Support;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tools;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using static Dusiburg.AI.Trasporto.Agents.Tests.Support.ScriptedChatClient;

namespace Dusiburg.AI.Trasporto.Agents.Tests;

/// <summary>Agente di mappatura con modello a copione e server MCP finto, sullo scenario 2 (<c>SH-1002</c>).</summary>
public class MappingAgentTests
{
    private const string Summary = "Ho proposto CAP, numero ONU, denominazione e peso lordo.";

    private const string Postcode = "consignee/postalAddress/postcode";
    private const string UnNumber = "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID";
    private const string ShippingName = "includedConsignmentItem[0]/transportDangerousGoods[0]/properShippingName";
    private const string GrossWeight = "grossWeight";

    private static readonly string DispatcherKey = FakeEftiTools.KeyFor(Role.Dispatcher);

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public async Task Run_ScenarioTwo_LeavesTheExpectedProposalsAndReportsToolFacts()
    {
        //SETUP
        var tools = new FakeEftiTools();
        var chat = new ScriptedChatClient(
        [
            .. ReadIssuesAndOrder(),
            Propose(Postcode, "69007"),
            Propose(UnNumber, "2014"),
            Propose(ShippingName, "PEROSSIDO DI IDROGENO IN SOLUZIONE ACQUOSA"),
            Propose(GrossWeight, "8840"),
            Text(Summary)
        ]);
        var models = new ScriptedChatClientFactory(chat);

        //SUT
        MappingRunResult result = await Agent(models, tools).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        Assert.That(result.Proposals.Select(p => (p.FieldPath, p.ProposedValue, p.Created)), Is.EqualTo(new[]
        {
            (Postcode, "69007", true),
            (UnNumber, "2014", true),
            (ShippingName, "PEROSSIDO DI IDROGENO IN SOLUZIONE ACQUOSA", true),
            (GrossWeight, "8840", true)
        }));
        Assert.That(result.Proposals.Select(p => p.ProposalId), Is.EqualTo(new[] { 1, 2, 3, 4 }), "gli identificativi sono quelli restituiti dal tool");
        Assert.That((result.ShipmentCode, result.DatasetStatus, result.Summary), Is.EqualTo(("SH-1002", (DatasetStatus?)DatasetStatus.Incomplete, Summary)));
        Assert.That(result.Discarded, Is.Empty);
        Assert.That(result.Warnings, Is.Empty);
        Assert.That(result.ToolCalls.Select(c => (c.Tool, c.Outcome)), Is.EqualTo(new[]
        {
            (EftiToolNames.ListValidationIssues, "ok"), (EftiToolNames.GetTmsOrder, "ok"),
            (EftiToolNames.ProposeFieldCompletion, "ok"), (EftiToolNames.ProposeFieldCompletion, "ok"),
            (EftiToolNames.ProposeFieldCompletion, "ok"), (EftiToolNames.ProposeFieldCompletion, "ok")
        }));
        Assert.That((result.Model.Provider, result.Model.Model, result.Model.InputTokens, result.Model.OutputTokens), Is.EqualTo(("stub", "scripted", (long?)100, (long?)10)));
        Assert.That(models.Tasks, Is.EqualTo(new[] { ModelTask.Mapping }));
        Assert.That(tools.OpenedWithKeys, Is.EqualTo(new[] { DispatcherKey }), "i tool si aprono con la chiave di chi ha avviato l'agente");
    }

    [Test]
    public async Task Run_WhateverTheModelWrites_ShipmentAndSourceAreThoseOfTheCode()
    {
        //SETUP
        var tools = new FakeEftiTools();
        var chat = new ScriptedChatClient(
        [
            Call(EftiToolNames.ListValidationIssues, new() { ["shipmentCode"] = "SH-9999" }),
            Call(EftiToolNames.GetTmsOrder, new() { ["shipmentCode"] = "SH-9999" }),
            Call(EftiToolNames.ProposeFieldCompletion, new()
            {
                ["shipmentCode"] = "SH-9999", ["fieldPath"] = Postcode, ["value"] = "69007", ["source"] = "Inference", ["rationale"] = "Nel testo."
            }),
            Text(Summary)
        ]);

        //SUT
        MappingRunResult result = await Agent(new ScriptedChatClientFactory(chat), tools).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        Dictionary<string, string?> proposal = tools.Proposals.Single();
        Assert.That(result.Proposals, Has.Count.EqualTo(1));
        Assert.That((proposal["shipmentCode"], proposal["source"]), Is.EqualTo(("SH-1002", nameof(ProposalSource.TmsOrder))));
        Assert.That(proposal["sourceReference"], Is.EqualTo("TO-1002"), "senza riferimento del modello vale il codice dell'ordine");
    }

    [Test]
    public async Task Run_OffersOnlyTheAllowedTools_WithoutTheInjectedArguments()
    {
        //SETUP
        var chat = new ScriptedChatClient([.. ReadIssuesAndOrder(), Text(Summary)]);

        //SUT
        await Agent(new ScriptedChatClientFactory(chat), new FakeEftiTools()).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        List<AIFunction> offered = [.. chat.Options[0]!.Tools!.OfType<AIFunction>()];
        string proposeSchema = offered.Single(t => t.Name == EftiToolNames.ProposeFieldCompletion).JsonSchema.GetRawText();
        Assert.That(offered.Select(t => t.Name), Is.EquivalentTo(MappingAgent.AllowedTools), "search_shipments e get_shipment_dataset non servono: non si offrono");
        Assert.That(proposeSchema, Does.Contain("fieldPath").And.Contain("rationale"));
        Assert.That(proposeSchema, Does.Not.Contain("shipmentCode").And.Not.Contain("\"source\""));
        Assert.That(chat.Options[0]!.Instructions, Does.Contain("Never guess"));
        Assert.That(chat.Options[0]!.ResponseFormat, Is.Null.Or.InstanceOf<ChatResponseFormatText>(), "nessuno schema di output nella fase di lavoro");
    }

    [Test]
    public async Task Run_ValueNotWrittenInTheOrder_IsDiscardedBeforeReachingTheServer()
    {
        //SETUP
        var tools = new FakeEftiTools();
        var chat = new ScriptedChatClient(
        [
            .. ReadIssuesAndOrder(),
            Propose(GrossWeight, "8.840"),
            Propose(GrossWeight, "8840"),
            Propose(Postcode, "69008"),
            Text(Summary)
        ]);

        //SUT
        MappingRunResult result = await Agent(new ScriptedChatClientFactory(chat), tools).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        Assert.That(result.Proposals.Select(p => (p.FieldPath, p.ProposedValue)), Is.EqualTo(new[] { (GrossWeight, "8840") }));
        Assert.That(result.Discarded.Select(d => (d.FieldPath, d.Value)), Is.EqualTo(new[] { ((string?)GrossWeight, (string?)"8.840"), (Postcode, "69008") }));
        Assert.That(result.Discarded[0].Reason, Does.StartWith(ErrorCodes.ValidationError).And.Contain("8840"));
        Assert.That(tools.Proposals.Select(p => p["value"]), Is.EqualTo(new[] { "8840" }), "al server arriva solo la proposta riconducibile all'ordine");
        Assert.That(chat.LastToolResult(3), Does.Contain(ErrorCodes.ValidationError).And.Contain("8840"), "il modello riceve il motivo e può correggersi");
        Assert.That(result.ToolCalls.Select(c => c.Outcome).Skip(2), Is.EqualTo(new[] { $"error:{ErrorCodes.ValidationError}", "ok", $"error:{ErrorCodes.ValidationError}" }));
    }

    [Test]
    public async Task Run_ProposalBeforeReadingTheOrder_IsRefused()
    {
        //SETUP
        var tools = new FakeEftiTools();
        var chat = new ScriptedChatClient(
        [
            Call(EftiToolNames.ListValidationIssues, new()),
            Propose(Postcode, "69007"),
            Text(Summary)
        ]);

        //SUT
        MappingRunResult result = await Agent(new ScriptedChatClientFactory(chat), tools).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        Assert.That(result.Proposals, Is.Empty);
        Assert.That(result.Discarded.Single().Reason, Does.Contain("get_tms_order"));
        Assert.That(tools.Proposals, Is.Empty);
    }

    [Test]
    public async Task Run_NumericValueWrittenAsANumber_IsProposedAsItWasWritten()
    {
        //SETUP
        var tools = new FakeEftiTools();
        var chat = new ScriptedChatClient(
        [
            .. ReadIssuesAndOrder(),
            Call(EftiToolNames.ProposeFieldCompletion, new() { ["fieldPath"] = GrossWeight, ["value"] = 8840, ["rationale"] = "Peso nelle istruzioni." }),
            Text(Summary)
        ]);

        //SUT
        MappingRunResult result = await Agent(new ScriptedChatClientFactory(chat), tools).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        Assert.That(result.Proposals.Select(p => p.ProposedValue), Is.EqualTo(new[] { "8840" }));
    }

    [Test]
    public async Task Run_ServerRefusesAProposal_ItIsReportedAsDiscardedWithTheServerReason()
    {
        //SETUP
        var tools = new FakeEftiTools();
        tools.ProposalErrors[Postcode] = (ErrorCodes.Conflict, "Il campo ha già il valore '69007'.");
        var chat = new ScriptedChatClient([.. ReadIssuesAndOrder(), Propose(Postcode, "69007"), Propose(UnNumber, "2014"), Propose(UnNumber, "2014"), Text(Summary)]);

        //SUT
        MappingRunResult result = await Agent(new ScriptedChatClientFactory(chat), tools).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        Assert.That(result.Proposals.Select(p => p.FieldPath), Is.EqualTo(new[] { UnNumber }), "la stessa proposta ripetuta compare una volta sola");
        Assert.That(result.Discarded.Select(d => (d.FieldPath, d.Reason)), Is.EqualTo(new[] { ((string?)Postcode, $"{ErrorCodes.Conflict}: Il campo ha già il valore '69007'.") }));
    }

    [Test]
    public async Task Run_ModelCallsAToolOutsideTheAllowList_TheToolIsNeverInvoked()
    {
        //SETUP
        var tools = new FakeEftiTools();
        var chat = new ScriptedChatClient(
        [
            Call(EftiToolNames.GetShipmentDataset, new() { ["shipmentCode"] = "SH-1002" }),
            Call("accept_proposal", new() { ["proposalId"] = 1 }),
            Text("Non posso fare altro.")
        ]);

        //SUT
        MappingRunResult result = await Agent(new ScriptedChatClientFactory(chat), tools).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        Assert.That(tools.Calls, Is.Empty, "il server finto ha quei tool, ma l'agente non può raggiungerli");
        Assert.That(result.Proposals, Is.Empty);
        Assert.That(result.ToolCalls, Is.Empty);
        Assert.That(result.Warnings, Has.Some.Contains("nessun tool"));
    }

    [Test]
    public async Task Run_ModelRefuses_StopsWithAWarningAndKeepsTheFacts()
    {
        //SETUP
        var chat = new ScriptedChatClient([.. ReadIssuesAndOrder(), Propose(Postcode, "69007"), Refusal()]);

        //SUT
        MappingRunResult result = await Agent(new ScriptedChatClientFactory(chat), new FakeEftiTools()).RunAsync("SH-1002", DispatcherKey, progress: null, CancellationToken);

        Assert.That(result.Summary, Is.Null);
        Assert.That(result.Warnings, Has.Some.Contains("rifiutato"));
        Assert.That(result.Proposals, Has.Count.EqualTo(1), "ciò che i tool avevano già fatto resta nell'esito");
        Assert.That(chat.RequestCount, Is.EqualTo(4), "nessun nuovo tentativo dopo un rifiuto");
    }

    [TestCase(Role.Authority)]
    [TestCase(Role.ClaimsManager)]
    [TestCase(Role.Admin)]
    public void Run_StartedByARoleWithoutTheTools_DoesNotStart(Role role)
    {
        //SETUP
        var chat = new ScriptedChatClient(Text(Summary));
        MappingAgent agent = Agent(new ScriptedChatClientFactory(chat), new FakeEftiTools());

        //SUT
        Assert.That(
            async () => await agent.RunAsync("SH-1002", FakeEftiTools.KeyFor(role), progress: null, CancellationToken),
            Throws.TypeOf<AgentUnavailableException>().With.Message.Contains(EftiToolNames.ProposeFieldCompletion));

        Assert.That(chat.RequestCount, Is.EqualTo(0), "senza i tool il modello non viene nemmeno chiamato");
    }

    [Test]
    public async Task Run_WithProgress_ReportsTheModelAndEveryToolCallInOrder()
    {
        //SETUP
        var steps = new RecordingProgress();
        var chat = new ScriptedChatClient([.. ReadIssuesAndOrder(), Propose(GrossWeight, "8.840"), Propose(GrossWeight, "8840"), Text(Summary)]);

        //SUT
        await Agent(new ScriptedChatClientFactory(chat), new FakeEftiTools()).RunAsync("SH-1002", DispatcherKey, steps, CancellationToken);

        Assert.That(steps.Steps.Select(s => (s.Kind, s.Tool, s.Outcome)), Is.EqualTo(new[]
        {
            (AgentStepKinds.Model, (string?)null, (string?)null),
            (AgentStepKinds.Tool, EftiToolNames.ListValidationIssues, "ok"),
            (AgentStepKinds.Tool, EftiToolNames.GetTmsOrder, "ok"),
            (AgentStepKinds.Tool, EftiToolNames.ProposeFieldCompletion, $"error:{ErrorCodes.ValidationError}"),
            (AgentStepKinds.Tool, EftiToolNames.ProposeFieldCompletion, "ok")
        }));
        Assert.That(steps.Steps[0].Message, Is.EqualTo("stub · scripted"));
        Assert.That(steps.Steps[3].Message, Does.Contain("8840"));
    }

    private static MappingAgent Agent(IChatClientFactory models, FakeEftiTools tools) => new(models, tools, NullLogger<MappingAgent>.Instance);

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse>[] ReadIssuesAndOrder() =>
    [
        Call(EftiToolNames.ListValidationIssues, new()),
        Call(EftiToolNames.GetTmsOrder, new())
    ];

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse> Propose(string fieldPath, string value) =>
        Call(EftiToolNames.ProposeFieldCompletion, new()
        {
            ["fieldPath"] = fieldPath, ["value"] = value, ["rationale"] = "Valore riportato nelle istruzioni del mittente.", ["sourceReference"] = "senderInstructions"
        });
}

/// <summary>Raccoglie i passi nell'ordine in cui arrivano, nel thread di chi li riporta.</summary>
internal sealed class RecordingProgress : IProgress<AgentStep>
{
    public List<AgentStep> Steps { get; } = [];

    public void Report(AgentStep value) => Steps.Add(value);
}
