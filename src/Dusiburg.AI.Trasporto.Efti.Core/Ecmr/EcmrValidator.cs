using System.Text.Json.Nodes;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Ecmr;

/// <summary>Il documento eCMR di una spedizione con il suo esito: JSON da salvare, stato, problemi trovati.</summary>
public sealed record EcmrEvaluation(JsonObject Document, string Json, EcmrStatus Status, IReadOnlyList<ValidationFinding> Findings)
{
    public int Errors => Findings.Count(f => f.Severity == IssueSeverity.Error);

    public int Warnings => Findings.Count(f => f.Severity == IssueSeverity.Warning);
}

/// <summary>
/// Valutazione deterministica del documento eCMR, parallela a <see cref="EftiValidator"/>: stessa spedizione, mappatura e
/// regole proprie. Nessun modello è coinvolto.
/// </summary>
/// <param name="countries">Lo schema eFTI, solo per la lista dei paesi ISO: è la stessa dello schema eCMR.</param>
public sealed class EcmrValidator(EcmrSchema schema, EcmrXmlExporter exporter, EftiSchemaCatalog countries)
{
    public static EcmrValidator CreateDefault()
    {
        EcmrSchema schema = EcmrSchema.Default;

        return new EcmrValidator(schema, new EcmrXmlExporter(schema), EftiSchemaCatalog.Default);
    }

    public string SchemaVersion => schema.Version;

    /// <summary>Solo le regole dichiarative, nell'ordine dell'elenco.</summary>
    public IReadOnlyList<ValidationFinding> ApplyRules(Shipment shipment)
    {
        var context = new ValidationContext(shipment, countries);

        return
        [
            .. EcmrRules.All.SelectMany(rule => rule.Check(context)
                .Select(violation => new ValidationFinding(rule.Code, rule.Severity, violation.FieldPath, violation.Message)))
        ];
    }

    /// <summary>
    /// Mappa la spedizione nel documento e lo valuta, per i trasporti nazionali come per gli internazionali.
    /// <see cref="EcmrStatus.Complete"/> solo senza problemi bloccanti e con l'XML conforme allo schema; gli avvisi non
    /// incidono sullo stato.
    /// </summary>
    public EcmrEvaluation Evaluate(Shipment shipment)
    {
        JsonObject document = EcmrMapper.Map(shipment);
        string json = EcmrMapper.Serialize(document);

        List<ValidationFinding> findings = [.. ApplyRules(shipment)];

        if (findings.All(f => f.Severity != IssueSeverity.Error))
        {
            findings.AddRange(schema.Validate(exporter.Export(json))
                .Select(error => new ValidationFinding(EcmrRules.SchemaRuleCode, IssueSeverity.Error, schema.RootElement.Name!, error)));
        }

        EcmrStatus status = findings.Any(f => f.Severity == IssueSeverity.Error) ? EcmrStatus.Draft : EcmrStatus.Complete;

        return new EcmrEvaluation(document, json, status, findings);
    }
}
