using System.Text.Json;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tools;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Agents.Tests.Support;

/// <summary>
/// Server MCP finto: i sette tool di <c>efti-mcp</c> in memoria, con gli stessi nomi e gli stessi argomenti, sui dati dello
/// scenario 2 (<c>SH-1002</c>) e dello scenario 4 (targa <c>ZZ104AA</c>). Registra gli argomenti ricevuti e mostra a ogni ruolo
/// solo i propri tool, come il server vero.
/// </summary>
internal sealed class FakeEftiTools : IAgentToolCatalog
{
    public const string Uil = "it-gate-sim:dusiburg-trasporto:01a10c7d-7306-77ea-ad13-29ade0157fbd";

    public const string SenderInstructions =
        "Merce ADR: UN 2014 PEROSSIDO DI IDROGENO IN SOLUZIONE ACQUOSA, classe 5.1 (8), GI II. " +
        "Consegna presso Papeteries du Rhône, 69007 Lyon, TVA FR99999000022. Peso lordo complessivo 8.840 kg.";

    private static readonly Dictionary<string, Role[]> Visibility = new()
    {
        [EftiToolNames.SearchShipments] = [Role.Dispatcher, Role.ClaimsManager, Role.Admin],
        [EftiToolNames.GetShipmentDataset] = [Role.Dispatcher, Role.ClaimsManager, Role.Admin],
        [EftiToolNames.ListValidationIssues] = [Role.Dispatcher, Role.ClaimsManager, Role.Admin],
        [EftiToolNames.GetTmsOrder] = [Role.Dispatcher],
        [EftiToolNames.ProposeFieldCompletion] = [Role.Dispatcher],
        [EftiToolNames.FindConsignmentsByVehicle] = [Role.Authority],
        [EftiToolNames.GetAuthoritySubset] = [Role.Authority]
    };

    private int _nextProposalId = 1;

    /// <summary>Campi per cui il server risponde con un errore, e quale.</summary>
    public Dictionary<string, (string Code, string Message)> ProposalErrors { get; } = [];

    /// <summary>Con <c>false</c> la ricerca per targa non trova nulla.</summary>
    public bool VehicleHasConsignment { get; set; } = true;

    public List<Dictionary<string, string?>> Proposals { get; } = [];

    public List<string> Calls { get; } = [];

    public List<string> SearchedPlates { get; } = [];

    public List<string?> OpenedWithKeys { get; } = [];

    public static string KeyFor(Role role) => $"chiave-di-test-{role.ToString().ToLowerInvariant()}";

    public Task<IAgentToolSession> OpenAsync(string apiKey, CancellationToken cancellationToken)
    {
        OpenedWithKeys.Add(apiKey);

        Role role = Enum.GetValues<Role>().Single(r => KeyFor(r) == apiKey);

        return Task.FromResult<IAgentToolSession>(new Session([.. All().Where(tool => Visibility[tool.Name].Contains(role))]));
    }

    private AIFunction[] All() =>
    [
        Tool(EftiToolNames.SearchShipments, (string? status = null) => Json(new { shipments = Array.Empty<object>() })),

        Tool(EftiToolNames.GetShipmentDataset, (string shipmentCode) => Json(new
        {
            shipmentCode, version = 1, status = "Incomplete", dataset = new { consignor = new { taxRegistration = new[] { new { id = "IT01000020006" } } } }
        })),

        Tool(EftiToolNames.ListValidationIssues, (string shipmentCode) => Json(new
        {
            shipmentCode,
            datasetVersion = 1,
            status = "Incomplete",
            issues = new[]
            {
                new { ruleCode = "M-CONSIGNEE-ADDRESS", severity = "Error", fieldPath = "consignee/postalAddress/postcode", message = "CAP mancante." },
                new { ruleCode = "M-ADR-UN-NUMBER", severity = "Error", fieldPath = "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", message = "Riga 1: numero ONU mancante." },
                new { ruleCode = "M-ADR-SHIPPING-NAME", severity = "Error", fieldPath = "includedConsignmentItem[0]/transportDangerousGoods[0]/properShippingName", message = "Riga 1: denominazione mancante." },
                new { ruleCode = "C-WEIGHT-SUM", severity = "Error", fieldPath = "grossWeight", message = "Peso lordo dichiarato 8480 kg, somma delle righe 8840 kg." }
            }
        })),

        Tool(EftiToolNames.GetTmsOrder, (string shipmentCode) => Json(new
        {
            shipmentCode,
            order = new
            {
                order = new { code = "TO-1002", totalPackages = 22, totalGrossWeightKg = 8480m, deliveryDate = "2026-10-08" },
                consignee = new { name = "Papeteries du Rhône SAS", vatNumber = (string?)null, street = "Rue de Gerland 112", postalCode = (string?)null, city = "Lyon", countryCode = "FR" },
                senderInstructions = SenderInstructions
            }
        })),

        Tool(EftiToolNames.ProposeFieldCompletion,
            (string shipmentCode, string fieldPath, string value, string source, string rationale, string? sourceReference = null) =>
            {
                if (ProposalErrors.TryGetValue(fieldPath, out (string Code, string Message) error))
                {
                    return Json(ToolErrorResponse.Create(error.Code, error.Message));
                }

                Dictionary<string, string?>? existing = Proposals.FirstOrDefault(p => p["shipmentCode"] == shipmentCode && p["fieldPath"] == fieldPath && p["value"] == value);
                bool created = existing is null;

                existing ??= new()
                {
                    ["proposalId"] = (_nextProposalId++).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["shipmentCode"] = shipmentCode,
                    ["fieldPath"] = fieldPath,
                    ["value"] = value,
                    ["source"] = source,
                    ["rationale"] = rationale,
                    ["sourceReference"] = sourceReference
                };

                if (created)
                {
                    Proposals.Add(existing);
                }

                return Json(new
                {
                    proposalId = int.Parse(existing["proposalId"]!, System.Globalization.CultureInfo.InvariantCulture),
                    created,
                    status = "Pending",
                    shipmentCode,
                    fieldPath,
                    proposedValue = value,
                    currentValue = (string?)null
                });
            }),

        Tool(EftiToolNames.FindConsignmentsByVehicle, (string plate) =>
        {
            SearchedPlates.Add(plate);

            return VehicleHasConsignment
                ? Json(new
                {
                    consignments = new[]
                    {
                        new
                        {
                            uil = Uil,
                            identifiers = new
                            {
                                carrierAcceptanceDateTime = new { value = "202610040830+0100", formatId = "205" },
                                mainCarriageTransportMovement = new[] { new { dangerousGoodsIndicator = true, modeCode = "3", usedTransportMeans = new { id = "ZZ104AA" } } }
                            }
                        }
                    }
                })
                : Json(new { consignments = Array.Empty<object>() });
        }),

        Tool(EftiToolNames.GetAuthoritySubset, (string uil, string subsetId) => Json(new
        {
            uil,
            subsetId,
            schemaVersion = "0.8",
            dataset = subsetId == "EU05a"
                ? (object)new
                {
                    consignee = new { name = new[] { "Química Levante S.L." } },
                    includedConsignmentItem = new[]
                    {
                        new
                        {
                            transportDangerousGoods = new[]
                            {
                                new { uNDGID = "1263", properShippingName = new[] { "PITTURE" }, hazardClassificationID = "3", packagingDangerLevelCode = "2", grossWeight = new { value = 4200m, unitId = "KGM" } }
                            }
                        }
                    }
                }
                : new
                {
                    consignor = new { name = new[] { "Imballaggi Tirreno S.r.l." } },
                    consignee = new { name = new[] { "Química Levante S.L." }, postalAddress = new { cityName = new[] { "València" }, countryCode = "ES" } },
                    grossWeight = new[] { new { value = 5300m, unitId = "KGM" } }
                }
        }))
    ];

    /// <summary>
    /// I tool restituiscono JSON, come i tool MCP veri. Ogni chiamata arrivata fin qui è registrata: una chiamata fermata dalla
    /// guardia non compare.
    /// </summary>
    private AIFunction Tool(string name, Delegate implementation)
    {
        AIFunction function = AIFunctionFactory.Create(implementation, name: name);

        return new RecordingFunction(function, Calls);
    }

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);

    private sealed class RecordingFunction(AIFunction inner, List<string> calls) : DelegatingAIFunction(inner)
    {
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            calls.Add(Name);

            return base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }

    private sealed class Session(IReadOnlyList<AIFunction> tools) : IAgentToolSession
    {
        public IReadOnlyList<AIFunction> Tools { get; } = tools;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
