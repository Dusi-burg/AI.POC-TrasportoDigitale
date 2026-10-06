using System.Net;
using Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests;

/// <summary>
/// Proposte di completamento e loro decisione via HTTP, su un database dedicato: proporre non tocca il dataset, accettare crea
/// la versione successiva e la pubblica sul gate se è valida. Più la ricerca delle spedizioni e l'ordine di origine nel TMS,
/// che servono a chi propone.
/// </summary>
public class FieldProposalTests
{
    private EftiApiTestHost _api = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task StartApiWithDemoShipments()
    {
        // SH-1001 e SH-1004 sono valide e sul gate; SH-1002 è lo scenario 2 (incompleta); SH-1005 serve solo a essere rotta.
        _api = await EftiApiTestHost.StartAsync(CancellationToken, "TO-1001", "TO-1002", "TO-1004", "TO-1005");
    }

    [OneTimeTearDown]
    public async Task StopApi() => await _api.DisposeAsync();

    [Test]
    public async Task Propose_CompletableField_IsRecordedAsPendingAndLeavesTheDatasetAlone()
    {
        //SETUP
        var request = new ProposeFieldRequest("consignee/postalAddress/streetName", " Carrer dels Fusters 11 ", "inference", null, "Numero civico corretto dal cliente.");

        //SUT
        ProposeFieldResponse response = await ProposeAsync("SH-1004", request);

        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);
        List<FieldProposalView> listed = await _api.GetAsync<List<FieldProposalView>>(Role.Admin, "/api/shipments/SH-1004/proposals", CancellationToken);
        FieldProposalView proposal = response.Proposal;
        Assert.That(response.Created, Is.True);
        Assert.That((proposal.ShipmentCode, proposal.FieldPath, proposal.ProposedValue, proposal.CurrentValue, proposal.Status, proposal.Source, proposal.DatasetVersion),
            Is.EqualTo(("SH-1004", "consignee/postalAddress/streetName", "Carrer dels Fusters 11", "Carrer dels Fusters 9", ProposalStatus.Pending, ProposalSource.Inference, 1)));
        Assert.That((proposal.ProposedById, proposal.ProposedAt, proposal.DecidedAt), Is.EqualTo((DemoActors.For(Role.Dispatcher).Id, EftiApiTestHost.Now, (DateTimeOffset?)null)));
        Assert.That(listed.Select(p => p.Id), Does.Contain(proposal.Id));
        Assert.That((audited.Operation, audited.Resource, audited.ShipmentCode, audited.Outcome), Is.EqualTo((Operation.FieldProposalCreate, "SH-1004", "SH-1004", AuditOutcome.Success)));
        Assert.That(await _api.DatasetsAsync("SH-1004", CancellationToken), Has.Count.EqualTo(1), "una proposta non crea versioni del dataset");
    }

    [Test]
    public async Task Propose_SameProposalTwice_ReturnsTheOneAlreadyPending()
    {
        //SETUP
        var request = new ProposeFieldRequest("transportContractDocument/id", "CMR-1004002", "Document", "CMR allegato", "Numero letto sulla lettera di vettura.");

        //SUT
        ProposeFieldResponse first = await ProposeAsync("SH-1004", request);
        ProposeFieldResponse second = await ProposeAsync("SH-1004", request with { Rationale = "Stessa proposta, altra motivazione." });
        ProposeFieldResponse otherValue = await ProposeAsync("SH-1004", request with { Value = "CMR-1004003" });

        Assert.That((first.Created, second.Created, otherValue.Created), Is.EqualTo((true, false, true)));
        Assert.That(second.Proposal, Is.EqualTo(first.Proposal));
        Assert.That(otherValue.Proposal.Id, Is.Not.EqualTo(first.Proposal.Id));
    }

    [TestCase("SH-1002", "carrier/name", "Altro Vettore S.r.l.", "TmsOrder", "motivo", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("SH-1002", "consignee/postalAddress/countryCode", "Francia", "TmsOrder", "motivo", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("SH-1002", "includedConsignmentItem[1]/transportDangerousGoods[0]/uNDGID", "2014", "TmsOrder", "motivo", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("SH-1002", "includedConsignmentItem[5]/transportDangerousGoods[0]/uNDGID", "2014", "TmsOrder", "motivo", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("SH-1002", "consignee/postalAddress/postcode", "69007", "TmsOrder", " ", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("SH-1002", "consignee/postalAddress/postcode", "69007", "Sentito dire", "motivo", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("SH-1002", "consignee/postalAddress/postcode", "69007", "1", "motivo", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("SH-1002", "consignee/postalAddress/cityName", "Lyon", "TmsOrder", "motivo", HttpStatusCode.Conflict, ErrorCodes.Conflict)]
    [TestCase("SH-9999", "consignee/postalAddress/postcode", "69007", "TmsOrder", "motivo", HttpStatusCode.NotFound, ErrorCodes.NotFound)]
    public async Task Propose_RequestThatCannotBecomeAProposal_IsRefused(
        string shipmentCode, string fieldPath, string value, string source, string rationale, HttpStatusCode expectedStatus, string expectedCode)
    {
        //SETUP
        var request = new ProposeFieldRequest(fieldPath, value, source, null, rationale);

        //SUT
        using HttpResponseMessage response = await _api.SendAsync(Role.Dispatcher, HttpMethod.Post, $"/api/shipments/{shipmentCode}/proposals", request, CancellationToken);

        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);
        Assert.That((response.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(response, CancellationToken)), Is.EqualTo((expectedStatus, expectedCode)));
        Assert.That((audited.Operation, audited.Outcome), Is.EqualTo((Operation.FieldProposalCreate, AuditOutcome.Failed)));
    }

    [Test]
    public async Task Decide_Rejected_ChangesOnlyTheProposal_AndCannotBeDecidedAgain()
    {
        //SETUP
        ProposeFieldResponse proposed = await ProposeAsync("SH-1004",
            new ProposeFieldRequest("numberOfPackages", "30", "Inference", null, "Conteggio dei colli da verificare."));
        string decisionPath = $"/api/proposals/{proposed.Proposal.Id}/decision";

        //SUT
        ProposalDecisionResponse rejected = await _api.PostAsync<ProposalDecisionResponse>(
            Role.Dispatcher, decisionPath, new ProposalDecisionRequest(ProposalStatus.Rejected, "I colli sono 28."), CancellationToken);
        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);
        using HttpResponseMessage again = await _api.SendAsync(Role.Dispatcher, HttpMethod.Post, decisionPath, new ProposalDecisionRequest(ProposalStatus.Accepted, null), CancellationToken);
        using HttpResponseMessage undecided = await _api.SendAsync(Role.Dispatcher, HttpMethod.Post, decisionPath, new ProposalDecisionRequest(ProposalStatus.Pending, null), CancellationToken);
        using HttpResponseMessage unknown = await _api.SendAsync(Role.Dispatcher, HttpMethod.Post, "/api/proposals/999999/decision", new ProposalDecisionRequest(ProposalStatus.Rejected, null), CancellationToken);

        FieldProposalView proposal = rejected.Proposal;
        Assert.That((proposal.Status, proposal.DecidedById, proposal.DecidedAt, proposal.DecisionNote, proposal.ResultingDatasetVersion),
            Is.EqualTo((ProposalStatus.Rejected, DemoActors.For(Role.Dispatcher).Id, (DateTimeOffset?)EftiApiTestHost.Now, "I colli sono 28.", (int?)null)));
        Assert.That((rejected.DatasetVersion, rejected.DatasetStatus, rejected.PublishedOnGate), Is.EqualTo((1, DatasetStatus.Valid, true)));
        Assert.That((audited.Operation, audited.ShipmentCode, audited.Outcome), Is.EqualTo((Operation.FieldProposalDecide, "SH-1004", AuditOutcome.Success)));
        Assert.That(audited.Detail, Does.Contain("numberOfPackages").And.Contain("Rejected"));
        Assert.That((again.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(again, CancellationToken)), Is.EqualTo((HttpStatusCode.Conflict, ErrorCodes.Conflict)));
        Assert.That(undecided.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "Pending non è una decisione");
        Assert.That((unknown.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(unknown, CancellationToken)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)));
        Assert.That(await _api.DatasetsAsync("SH-1004", CancellationToken), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ScenarioTwo_AcceptingTheProposals_MakesTheDatasetValidAndPublishesItOnTheGate()
    {
        //SETUP
        ShipmentDetailView before = await _api.GetAsync<ShipmentDetailView>(Role.Dispatcher, "/api/views/shipments/SH-1002", CancellationToken);
        List<ConsignmentIdentifiersView> onGateBefore = await FindOnGateAsync("ZZ102AA");
        (string FieldPath, string Value)[] completions =
        [
            ("consignee/postalAddress/postcode", "69007"),
            ("includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", "2014"),
            ("includedConsignmentItem[0]/transportDangerousGoods[0]/properShippingName", "PEROSSIDO DI IDROGENO IN SOLUZIONE ACQUOSA"),
            ("grossWeight", "8840")
        ];
        List<ProposalDecisionResponse> decisions = [];

        //SUT
        foreach ((string fieldPath, string value) in completions)
        {
            ProposeFieldResponse proposed = await ProposeAsync("SH-1002",
                new ProposeFieldRequest(fieldPath, value, "TmsOrder", "TO-1002: senderInstructions", "Il valore è nelle istruzioni del mittente."));

            decisions.Add(await _api.PostAsync<ProposalDecisionResponse>(
                Role.Dispatcher, $"/api/proposals/{proposed.Proposal.Id}/decision", new ProposalDecisionRequest(ProposalStatus.Accepted, null), CancellationToken));
        }

        ShipmentDetailView after = await _api.GetAsync<ShipmentDetailView>(Role.Dispatcher, "/api/views/shipments/SH-1002", CancellationToken);
        List<ConsignmentIdentifiersView> onGateAfter = await FindOnGateAsync("ZZ102AA");
        AuthoritySubsetView dangerousGoods = await _api.GetAsync<AuthoritySubsetView>(
            Role.Authority, $"/api/gate/subset?uil={onGateAfter.Single().Uil}&subsetId=EU05a", CancellationToken);
        List<EftiDataset> versions = await _api.DatasetsAsync("SH-1002", CancellationToken);

        Assert.That((before.Dataset.Version, before.Dataset.Status, before.Shipment.Errors), Is.EqualTo((1, DatasetStatus.Incomplete, 4)));
        Assert.That(onGateBefore, Is.Empty, "un dataset incompleto non è sul gate");
        Assert.That(decisions.Select(d => (d.DatasetVersion, d.DatasetStatus, d.Errors, d.PublishedOnGate)), Is.EqualTo(new[]
        {
            (2, DatasetStatus.Incomplete, 3, false),
            (3, DatasetStatus.Incomplete, 2, false),
            (4, DatasetStatus.Incomplete, 1, false),
            (5, DatasetStatus.Valid, 0, true)
        }));
        Assert.That(decisions.Select(d => (d.Proposal.Status, d.Proposal.ResultingDatasetVersion)), Is.EqualTo(new[]
        {
            (ProposalStatus.Accepted, (int?)2), (ProposalStatus.Accepted, (int?)3), (ProposalStatus.Accepted, (int?)4), (ProposalStatus.Accepted, (int?)5)
        }));
        Assert.That((after.Dataset.Version, after.Dataset.Status, after.Shipment.Errors, after.TotalGrossWeightKg), Is.EqualTo((5, DatasetStatus.Valid, 0, (decimal?)8840m)));
        Assert.That(after.Parties.Single(p => p.Role == PartyRole.Consignee).PostalCode, Is.EqualTo("69007"));
        Assert.That(versions.Select(v => (v.Version, v.Status)), Is.EqualTo(new[]
        {
            (1, DatasetStatus.Incomplete), (2, DatasetStatus.Incomplete), (3, DatasetStatus.Incomplete), (4, DatasetStatus.Incomplete), (5, DatasetStatus.Valid)
        }), "le versioni precedenti restano com'erano");
        Assert.That(versions[0].Json, Does.Not.Contain("uNDGID"));
        Assert.That(versions[4].Json, Does.Contain("uNDGID"));
        Assert.That(onGateAfter, Has.Count.EqualTo(1), "il dataset valido è pubblicato: l'autorità trova la targa");
        Assert.That(dangerousGoods.Dataset.GetProperty("includedConsignmentItem")[0].GetProperty("transportDangerousGoods")[0].GetProperty("uNDGID").GetString(), Is.EqualTo("2014"));
    }

    [Test]
    public async Task Decide_AcceptedValueThatBreaksAValidDataset_MakesItIncompleteAndUnreadableByUil()
    {
        //SETUP
        string uil = (await FindOnGateAsync("ZZ105AA")).Single().Uil;
        ProposeFieldResponse proposed = await ProposeAsync("SH-1005",
            new ProposeFieldRequest("grossWeight", "36", "Inference", null, "Peso riportato male: prova di una decisione sbagliata."));

        //SUT
        ProposalDecisionResponse accepted = await _api.PostAsync<ProposalDecisionResponse>(
            Role.Dispatcher, $"/api/proposals/{proposed.Proposal.Id}/decision", new ProposalDecisionRequest(ProposalStatus.Accepted, null), CancellationToken);
        using HttpResponseMessage subset = await _api.SendAsync(Role.Authority, HttpMethod.Get, $"/api/gate/subset?uil={uil}&subsetId=EU03", body: null, CancellationToken);

        Assert.That((accepted.DatasetVersion, accepted.DatasetStatus, accepted.PublishedOnGate), Is.EqualTo((2, DatasetStatus.Incomplete, false)));
        Assert.That(accepted.Errors, Is.EqualTo(1), "C-WEIGHT-SUM: il peso dichiarato non coincide più con le righe");
        Assert.That((subset.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(subset, CancellationToken)), Is.EqualTo((HttpStatusCode.Conflict, ErrorCodes.Conflict)),
            "la voce resta nel registro, ma un dataset non più valido non si consulta");
    }

    [Test]
    public async Task List_ByCustomerAndPeriod_FiltersTheShipments()
    {
        //SUT
        List<ShipmentSummaryView> byConsignee = await ListAsync("customer=Rh%C3%B4ne");
        List<ShipmentSummaryView> byConsignor = await ListAsync("customer=Brambilla");
        List<ShipmentSummaryView> fromTomorrow = await ListAsync("from=2026-10-06");
        List<ShipmentSummaryView> yesterday = await ListAsync("from=2026-10-04&to=2026-10-04");
        List<ShipmentSummaryView> combined = await ListAsync("customer=Tirreno&to=2026-10-04&status=Valid");
        using HttpResponseMessage invalid = await _api.SendAsync(Role.Dispatcher, HttpMethod.Get, "/api/views/shipments?from=06/10/2026", body: null, CancellationToken);

        Assert.That(byConsignee.Select(s => s.Code), Is.EqualTo(new[] { "SH-1002" }));
        Assert.That(byConsignor.Select(s => s.Code), Is.EqualTo(new[] { "SH-1001" }));
        Assert.That(fromTomorrow.Select(s => s.Code), Is.EqualTo(new[] { "SH-1002" }), "SH-1002 parte domani, le altre sono già partite");
        Assert.That(yesterday.Select(s => s.Code), Is.EquivalentTo(new[] { "SH-1001", "SH-1004" }));
        Assert.That(combined.Select(s => s.Code), Is.EqualTo(new[] { "SH-1004" }));
        Assert.That((invalid.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(invalid, CancellationToken)), Is.EqualTo((HttpStatusCode.BadRequest, ErrorCodes.ValidationError)));
    }

    [Test]
    public async Task TmsOrder_OfAShipment_ReturnsTheSourceOrderAndIsAudited()
    {
        //SUT
        TransportOrderDetailView order = await _api.GetAsync<TransportOrderDetailView>(Role.Dispatcher, "/api/shipments/SH-1002/tms-order", CancellationToken);
        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);
        using HttpResponseMessage unknown = await _api.SendAsync(Role.Dispatcher, HttpMethod.Get, "/api/shipments/SH-9999/tms-order", body: null, CancellationToken);

        Assert.That(order.Order.Code, Is.EqualTo("TO-1002"));
        Assert.That(order.SenderInstructions, Does.Contain("UN 2014").And.Contain("69007"));
        Assert.That((audited.Operation, audited.Resource, audited.ShipmentCode, audited.Detail, audited.Outcome),
            Is.EqualTo((Operation.TmsOrderRead, "SH-1002", "SH-1002", "TO-1002", AuditOutcome.Success)));
        Assert.That((unknown.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(unknown, CancellationToken)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)));
    }

    private Task<ProposeFieldResponse> ProposeAsync(string shipmentCode, ProposeFieldRequest request) =>
        _api.PostAsync<ProposeFieldResponse>(Role.Dispatcher, $"/api/shipments/{shipmentCode}/proposals", request, CancellationToken);

    private Task<List<ConsignmentIdentifiersView>> FindOnGateAsync(string plate) =>
        _api.GetAsync<List<ConsignmentIdentifiersView>>(Role.Authority, $"/api/gate/identifiers?plate={plate}", CancellationToken);

    private Task<List<ShipmentSummaryView>> ListAsync(string query) =>
        _api.GetAsync<List<ShipmentSummaryView>>(Role.Dispatcher, $"/api/views/shipments?{query}", CancellationToken);
}
