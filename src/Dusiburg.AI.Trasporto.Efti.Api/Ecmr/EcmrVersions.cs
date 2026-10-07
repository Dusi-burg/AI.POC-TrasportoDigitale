using Dusiburg.AI.Trasporto.Efti.Core.Ecmr;
using Dusiburg.AI.Trasporto.Efti.Data.Configurations;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;

namespace Dusiburg.AI.Trasporto.Efti.Api.Ecmr;

/// <summary>
/// Dalla valutazione dell'eCMR alla riga da salvare. La usano i due punti in cui nasce una versione del dataset eFTI:
/// importazione e decisione su una proposta.
/// </summary>
internal static class EcmrVersions
{
    public static EcmrDocument Create(EcmrEvaluation evaluation, string schemaVersion, int version, DateTimeOffset createdAt) => new()
    {
        Version = version,
        Status = evaluation.Status,
        SchemaVersion = schemaVersion,
        Json = evaluation.Json,
        CreatedAt = createdAt,
        Issues =
        [
            .. evaluation.Findings.Select(finding => new EcmrIssue
            {
                RuleCode = finding.RuleCode,
                Severity = finding.Severity,
                FieldPath = finding.FieldPath,
                // I messaggi dell'XSD possono superare la colonna.
                Message = finding.Message.Length <= ColumnSizes.IssueMessage ? finding.Message : finding.Message[..ColumnSizes.IssueMessage]
            })
        ]
    };
}
