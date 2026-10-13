using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;

namespace Dusiburg.AI.Trasporto.Web.Tests.Support;

/// <summary>Risposte dei servizi a valle per i test delle pagine: gli stessi contratti che scambiano i servizi veri.</summary>
internal static class TestData
{
    public const string Uil = "urn:efti:demo:gate-it:platform-dusiburg:3f2b8c1e";

    public const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly DateTimeOffset Moment = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    public static ShipmentSummaryView Summary(string code = "SH-1002", DatasetStatus status = DatasetStatus.Incomplete) => new(
        code, code.Replace("SH-", "TO-"), status, 1, status == DatasetStatus.Incomplete ? 4 : 0, 1,
        "Cartiera del Brenta S.p.A.", "Papeteries du Rhône SAS", "Vigonza", "IT", "Lyon", "FR",
        new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 10), true, "ZZ102AA", Moment);

    public static ShipmentDetailView Shipment(string code = "SH-1002", DatasetStatus status = DatasetStatus.Incomplete) => new(
        Summary(code, status),
        Guid.Parse("3f2b8c1e-0000-4000-8000-000000001002"),
        new LocationView("Via Riviera 3", "35010", "Vigonza", "IT"),
        new LocationView("Rue de Gerland 112", null, "Lyon", "FR"),
        null, null, 22, 8_480m, 17.6m, "IT", "XZ102AA", "IT", null, 1_980m, FreightPayment.Prepaid, "EUR", null, null, null,
        "Merce ADR: UN 2014, classe 5.1.",
        [
            new ShipmentPartyView(PartyRole.Consignor, 1, "Cartiera del Brenta S.p.A.", "IT01000020006", "Via Riviera 3", "35010", "Vigonza", "IT"),
            new ShipmentPartyView(PartyRole.Consignee, 1, "Papeteries du Rhône SAS", null, "Rue de Gerland 112", null, "Lyon", "FR")
        ],
        [new ConsignmentItemView(1, "Additivo sbiancante per cellulosa", 12, PackageType.Drum, 2_640m, 3.6m, true, null, null, "5.1", null)],
        new EftiDatasetView(1, status, "eFTI-demo-1.0", Moment, JsonSerializer.SerializeToElement(new { consignment = new { carrierAcceptanceLocation = "Vigonza" } })),
        status == DatasetStatus.Incomplete
            ? [new ValidationIssueView("ADR-002", IssueSeverity.Error, "consignment.items[1].dangerousGoods.unNumber", "Manca il numero ONU della merce pericolosa.")]
            : []);

    public static EcmrDocumentView Ecmr(string code = "SH-1002", EcmrStatus status = EcmrStatus.Draft) => new(
        code, 1, status, "100.D25A", Moment, status == EcmrStatus.Draft ? 1 : 0, 0,
        JsonDocument.Parse("""{ "ExchangedDocument": { "TypeCode": "730" }, "SpecifiedSupplyChainConsignment": { "ID": "SH" } }""").RootElement,
        status == EcmrStatus.Draft
            ? [new ValidationIssueView("E-NUMBER", IssueSeverity.Error, "ExchangedDocument/ID", "Numero della lettera di vettura mancante: non è ancora stata emessa.")]
            : []);

    public static FieldProposalView Proposal(int id = 7, ProposalStatus status = ProposalStatus.Pending) => new(
        id, "SH-1002", "consignment.items[1].dangerousGoods.unNumber", "2014", null, 1, ProposalSource.TmsOrder, "TO-1002: senderInstructions",
        "Il numero ONU è scritto nelle istruzioni del mittente.", status, "agent-mapping", "Agente di mappatura", Moment, null, null, null, null, null);

    public static ProposalDecisionResponse ProposalDecision(ProposalStatus status) =>
        new(Proposal(7, status), status == ProposalStatus.Accepted ? 2 : 1, DatasetStatus.Incomplete, 3, 1, PublishedOnGate: false);

    public static ExtractionFieldView Field(int id, string label, string value, ExtractionFieldOutcome outcome, decimal confidence, bool? annotation = null) => new(
        id, "consignment.totalGrossWeight", "11", label, value, value, "2400", new ConfidenceSignals(true, true, false, true, annotation), confidence, outcome,
        outcome == ExtractionFieldOutcome.Proposed ? 9 : null, outcome == ExtractionFieldOutcome.InReview ? 3 : null,
        outcome == ExtractionFieldOutcome.InReview ? "Confidenza sotto soglia." : null);

    public static DocumentSummaryView DocumentSummary(int id = 5, string shipmentCode = "SH-1003") =>
        new(id, shipmentCode, "CMR-1003.pdf", 48_211, "Giulia Ferri", Moment, 3, 1, 1, 1);

    public static DocumentDetailView Document(int id = 5, string shipmentCode = "SH-1003") => new(
        DocumentSummary(id, shipmentCode),
        "LETTERA DI VETTURA INTERNAZIONALE\n11 Peso lordo kg 2.460",
        ["peso verificato in pesa: 2.460 kg"],
        new ExtractionResultView("ollama", "qwen3.5:9b", 2, 0.80m, 24_500, 3_100, 420, Moment,
        [
            Field(1, "Numero della lettera di vettura", "CMR-1003001", ExtractionFieldOutcome.Matches, 1.00m),
            Field(2, "Numero di colli", "18", ExtractionFieldOutcome.Proposed, 0.85m),
            Field(3, "Peso lordo", "2460", ExtractionFieldOutcome.InReview, 0.54m, annotation: true)
        ]));

    public static ReviewTaskView ReviewTask(ReviewStatus status = ReviewStatus.Pending) => new(
        3, 5, "SH-1003", "CMR-1003.pdf", Field(3, "Peso lordo", "2460", ExtractionFieldOutcome.InReview, 0.54m), status,
        status == ReviewStatus.Pending ? null : "Giulia Ferri", status == ReviewStatus.Pending ? null : Moment, null, null);

    public static ReviewDecisionResponse ReviewConfirmed() => new(
        ReviewTask(ReviewStatus.Confirmed),
        new ProposalDecisionResponse(Proposal(11, ProposalStatus.Accepted) with { ShipmentCode = "SH-1003", ProposedValue = "2455" }, 3, DatasetStatus.Valid, 0, 0, true));

    public static AuditEventView Audit(long id, Role role, Operation operation, AuditOutcome outcome, string? reason = null) => new(
        id, Moment, $"u-{role.ToString().ToLowerInvariant()}", DemoActors.For(role).Name, role, operation, "SH-1004", "SH-1004", outcome, reason,
        outcome == AuditOutcome.Success ? "sottoinsieme EU03" : null, TraceId);

    public static ConsignmentIdentifiersView Identifiers() =>
        new(Uil, JsonSerializer.SerializeToElement(new { usedTransportEquipment = new[] { new { id = "ZZ104AA" } } }));

    public static AuthoritySubsetView Subset(string subsetId = "EU03") =>
        new(Uil, subsetId, "eFTI-demo-1.0", JsonSerializer.SerializeToElement(new { consignment = new { dangerousGoods = new { unNumber = "1263" } } }));

    /// <summary>Un sottoinsieme con la forma vera del dataset: misure con unità, momento nel formato 205, valori in array.</summary>
    public static AuthoritySubsetView RealisticSubset() => new(Uil, "EU03", "eFTI-demo-1.0", JsonDocument.Parse(
        """
        {
          "consignor": { "name": ["Imballaggi Tirreno S.r.l."], "postalAddress": { "cityName": ["Livorno"], "countryCode": "IT", "postcode": ["57121"], "streetName": ["Via Aurelia 150"] } },
          "consignee": { "name": ["Química Levante S.L."], "postalAddress": { "cityName": ["València"], "countryCode": "ES", "postcode": ["46014"], "streetName": ["Carrer dels Fusters 9"] } },
          "carrierAcceptanceDateTime": { "value": "202610050830+0100", "formatId": "205" },
          "carrierAcceptanceLocation": { "postalAddress": { "cityName": ["Livorno"], "countryCode": "IT" } },
          "grossVolume": [ { "value": 18.200, "unitId": "MTQ" } ],
          "grossWeight": [ { "value": 5300.000, "unitId": "KGM" } ],
          "natureIdentificationCargo": { "identificationText": ["Vernici industriali a solvente", "Imballaggi in cartone ondulato"] },
          "includedConsignmentItem": [ { "transportDangerousGoods": [ {
            "dangerousGoodsLogisticsPackage": [ { "itemQuantity": 20, "typeCode": ["DR"] } ],
            "grossWeight": { "value": 4200.000, "unitId": "KGM" },
            "hazardClassificationID": "3", "packagingDangerLevelCode": "2", "properShippingName": ["PITTURE"], "uNDGID": "1263" } ] } ],
          "mainCarriageTransportMovement": [ { "usedTransportMeans": { "id": "ZZ104AA", "registrationCountry": { "code": "IT" }, "typeCode": "33" } } ],
          "usedTransportEquipment": [ { "categoryCode": "SM", "id": "XZ104AA", "registrationCountry": { "code": "IT" } } ]
        }
        """).RootElement.Clone());

    public static SettingsView Settings() => new(
        [new TaskModelView("Mapping", "ollama", "qwen3.5:9b"), new TaskModelView("Authority", "anthropic", "claude-haiku-4-5")],
        new ExtractionSettingsView(0.35m, 0.15m, 0.30m, 0.20m, 0.30m, 0.80m, 2, false),
        7_200);

    public static ProbeReport Health() => new(
    [
        new ProbeResult(1, "Connessione SQL", ProbeStatus.Ok, "(localdb)\\localdev", TimeSpan.FromMilliseconds(12)),
        new ProbeResult(9, "Chiavi di ruolo", ProbeStatus.Error, "manca ApiKeys:Admin", TimeSpan.Zero)
    ]);

    public static MappingRunResult MappingResult() => new(
        "SH-1002", DatasetStatus.Incomplete,
        [new MappingProposal(7, "consignment.items[1].dangerousGoods.unNumber", "2014", null, true)],
        [new DiscardedProposal("consignment.cmrNumber", "CMR-0000", "Il valore non si ritrova nell'ordine.")],
        "Ho proposto il numero ONU.",
        [new AgentToolCall("get_validation_issues", "ok", 35)],
        new AgentModelInfo("ollama", "qwen3.5:9b", 2_100, 180, 41_000),
        []);

    public static string Sse(string eventName, object data) => $"event: {eventName}\ndata: {JsonSerializer.Serialize(data, data.GetType(), TrasportoJson.Options)}\n\n";
}
