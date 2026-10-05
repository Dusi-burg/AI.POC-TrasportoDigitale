using System.Xml.Linq;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Validation;

/// <summary>Il dataset di una spedizione con il suo esito: JSON da salvare, stato, problemi trovati.</summary>
public sealed record DatasetEvaluation(
    EftiConsignment Dataset, string Json, DatasetStatus Status, IReadOnlyList<ValidationFinding> Findings)
{
    public int Errors => Findings.Count(f => f.Severity == IssueSeverity.Error);

    public int Warnings => Findings.Count(f => f.Severity == IssueSeverity.Warning);
}

/// <summary>
/// Validazione deterministica del dataset eFTI: applica le regole dichiarative e, se nessuna regola bloccante è violata,
/// controlla che l'XML esportato sia conforme allo schema. Nessun modello è coinvolto.
/// </summary>
public sealed class EftiValidator(EftiSchemaCatalog catalog, EftiXmlExporter exporter, EftiXmlValidator xmlValidator)
{
    public static EftiValidator CreateDefault()
    {
        EftiSchemaCatalog catalog = EftiSchemaCatalog.Default;

        return new EftiValidator(catalog, new EftiXmlExporter(catalog), new EftiXmlValidator(catalog));
    }

    public string SchemaVersion => catalog.Version;

    /// <summary>Solo le regole dichiarative, nell'ordine dell'elenco.</summary>
    public IReadOnlyList<ValidationFinding> ApplyRules(Shipment shipment)
    {
        var context = new ValidationContext(shipment, catalog);

        return
        [
            .. ValidationRules.All.SelectMany(rule => rule.Check(context)
                .Select(violation => new ValidationFinding(rule.Code, rule.Severity, violation.FieldPath, violation.Message)))
        ];
    }

    /// <summary>
    /// Mappa la spedizione nel dataset e lo valuta. <see cref="DatasetStatus.Valid"/> solo senza problemi bloccanti e con
    /// l'XML conforme allo schema; gli avvisi non incidono sullo stato.
    /// </summary>
    public DatasetEvaluation Evaluate(Shipment shipment)
    {
        EftiConsignment dataset = EftiDatasetMapper.Map(shipment);
        string json = EftiDatasetJson.Serialize(dataset);
        List<ValidationFinding> findings = [.. ApplyRules(shipment)];

        if (findings.All(f => f.Severity != IssueSeverity.Error))
        {
            findings.AddRange(SchemaFindings(exporter.Export(json)));
        }

        DatasetStatus status = findings.Any(f => f.Severity == IssueSeverity.Error) ? DatasetStatus.Incomplete : DatasetStatus.Valid;

        return new DatasetEvaluation(dataset, json, status, findings);
    }

    private IEnumerable<ValidationFinding> SchemaFindings(XDocument document) =>
        xmlValidator.Validate(document)
            .Select(error => new ValidationFinding(ValidationRules.SchemaRuleCode, IssueSeverity.Error, catalog.RootElementName, error));
}
