using System.Text;
using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Eval.Reports;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Eval.Extraction;

/// <summary>Il report della valutazione dell'estrazione (<c>eval extraction</c>): tutto calcolato dalle letture salvate.</summary>
public static class ExtractionReport
{
    public static string Render(ExtractionEvaluation evaluation, ExtractionOptions options, ModelPrices prices)
    {
        ExtractionRunFile run = evaluation.Run;
        var metrics = new ExtractionMetrics(evaluation.Score(options));
        var report = new StringBuilder();

        report.Append($"# Valutazione dell'estrazione dai CMR — {run.CreatedAt}\n\n");
        report.Append("> Generato da `Eval -- eval extraction`. Le letture del modello sono salvate accanto al report: con `--replay` il report si ricalcola senza richiamare il modello.\n\n");

        AppendParameters(report, evaluation, options, prices);
        AppendSummary(report, metrics);
        AppendAccuracy(report, "## Accuratezza per gruppo di campi", "Gruppo", metrics.ByGroup);
        AppendAccuracy(report, "## Accuratezza per categoria di documento", "Categoria", metrics.ByCategory);
        AppendCalibration(report, metrics, options);
        AppendOutcomes(report, metrics);
        AppendErrors(report, metrics);
        AppendSensitivity(report, evaluation, options);
        AppendDocuments(report, evaluation, metrics);

        return report.ToString();
    }

    private static void AppendParameters(StringBuilder report, ExtractionEvaluation evaluation, ExtractionOptions options, ModelPrices prices)
    {
        ExtractionRunFile run = evaluation.Run;
        int documents = evaluation.Documents.Count;
        decimal? cost = prices.Cost(run.Candidate, run.InputTokens, run.OutputTokens);
        ConfidenceWeights weights = options.Weights;

        report.Append("| Parametro | Valore |\n|---|---|\n");
        report.Append($"| Modello | `{run.Model}` ({run.Provider}) |\n");
        report.Append($"| Documenti del golden set | {documents} ({string.Join(", ", evaluation.Documents.GroupBy(d => d.Golden.Category).Select(c => $"{c.Key} {c.Count()}"))}) |\n");
        report.Append($"| Letture per documento | {run.Passes} |\n");
        report.Append($"| Soglia di revisione | {Md.Number(options.ReviewThreshold)} |\n");
        report.Append($"| Pesi dei segnali | `Literal` {Md.Number(weights.Literal)}, `Format` {Md.Number(weights.Format)}, `Tms` {Md.Number(weights.Tms)}, `Agreement` {Md.Number(weights.Agreement)}, `Annotation` {Md.Number(weights.Annotation)} |\n");
        report.Append($"| Durata | {Md.Seconds(run.ElapsedMs)} in tutto, {Md.Seconds(documents == 0 ? 0 : run.ElapsedMs / documents)} a documento |\n");
        report.Append($"| Token (ingresso / uscita) | {Md.Number(run.InputTokens)} / {Md.Number(run.OutputTokens)} |\n");
        report.Append(run.Candidate.IsLocal
            ? "| Costo | modello locale: nessun costo a consumo |\n"
            : $"| Costo | {Md.Usd(cost)} in tutto, {Md.Usd(cost is null || documents == 0 ? null : cost / documents)} a documento (listino al {ModelPrices.DefaultsAsOf}) |\n");
        report.Append('\n');
    }

    private static void AppendSummary(StringBuilder report, ExtractionMetrics metrics)
    {
        AccuracyRow overall = metrics.Overall;
        MustReviewSummary mustReview = metrics.MustReview;

        report.Append("## Sintesi\n\n");
        report.Append("| Misura | Valore | Che cosa dice |\n|---|---|---|\n");
        report.Append($"| Accuratezza per campo | **{Md.Share(overall.Exact, overall.Expected)}** | campi attesi restituiti con il valore giusto |\n");
        report.Append($"| Campi errati / mancanti / in più | {overall.Wrong} / {overall.Missing} / {overall.Extra} | in più = un valore dove il documento non ne ha |\n");
        report.Append($"| **Errori passati sopra soglia** | **{metrics.WrongProposals.Count}** | valori sbagliati diventati proposta: è l'errore che conta |\n");
        report.Append($"| Errori non visti | {metrics.WrongMatches.Count} | valori sbagliati che coincidono con il TMS |\n");
        report.Append($"| Campi in revisione | {Md.Share(metrics.InReview, metrics.Judged)} | di cui {metrics.ExactInReview} giusti e non da far vedere: lavoro in più |\n");
        report.Append($"| Campi da far vedere a una persona | {Md.Share(mustReview.InReview, mustReview.Total)} in revisione | {mustReview.Proposed} diventati proposta, {mustReview.Matches} coincidono con il TMS, {mustReview.NotExtracted} non estratti |\n");
        report.Append($"| Valori iniettati usciti | {metrics.InjectedValues.Count} | campi in cui è uscito il valore che un'iniezione cercava di imporre |\n");
        report.Append($"| Calibrazione | {(metrics.IsCalibrationMonotonic ? "crescente" : "**non crescente**")} | l'accuratezza sale con la fascia di confidenza |\n\n");
    }

    private static void AppendAccuracy(StringBuilder report, string title, string firstColumn, IReadOnlyList<AccuracyRow> rows)
    {
        report.Append(title).Append("\n\n");
        report.Append(Md.Table(
            [firstColumn, "Attesi", "Esatti", "Errati", "Mancanti", "In più", "Accuratezza"],
            rows.Select(row => (IReadOnlyList<string>)
            [
                row.Name, $"{row.Expected}", $"{row.Exact}", $"{row.Wrong}", $"{row.Missing}", $"{row.Extra}", Md.Percent(row.Exact, row.Expected)
            ])));
        report.Append('\n');
    }

    private static void AppendCalibration(StringBuilder report, ExtractionMetrics metrics, ExtractionOptions options)
    {
        report.Append("## Calibrazione della confidenza\n\n");
        report.Append($"Campi giudicati dal codice ({metrics.Judged}: estratti ed esistenti sulla spedizione), per fascia di confidenza. La soglia è {Md.Number(options.ReviewThreshold)}: sotto, il campo va in revisione.\n\n");
        report.Append(Md.Table(
            ["Confidenza", "Campi", "Esatti", "Accuratezza"],
            metrics.Calibration.Select(bucket => (IReadOnlyList<string>)
            [
                bucket.Label, $"{bucket.Fields}", $"{bucket.Exact}", Md.Percent(bucket.Exact, bucket.Fields)
            ])));
        report.Append('\n');
    }

    private static void AppendOutcomes(StringBuilder report, ExtractionMetrics metrics)
    {
        IEnumerable<IReadOnlyList<string>> Rows()
        {
            foreach (ExtractionFieldOutcome outcome in new[] { ExtractionFieldOutcome.Matches, ExtractionFieldOutcome.Proposed, ExtractionFieldOutcome.InReview })
            {
                FieldScore[] fields = [.. metrics.Scores.Where(s => s.Outcome == outcome)];

                yield return
                [
                    Label(outcome), $"{fields.Length}", $"{fields.Count(s => s.Verdict == FieldVerdict.Exact)}",
                    $"{fields.Count(s => s.Verdict != FieldVerdict.Exact)}"
                ];
            }
        }

        report.Append("## Che fine fanno i campi\n\n");
        report.Append(Md.Table(["Esito deciso dal codice", "Campi", "Con il valore giusto", "Con un valore sbagliato"], Rows()));
        report.Append($"\nAltri {metrics.NotApplicable} valori riguardano campi che sulla spedizione non esistono (per esempio dati ADR su una riga non pericolosa) e non producono nulla.\n\n");
    }

    private static void AppendErrors(StringBuilder report, ExtractionMetrics metrics)
    {
        report.Append("## Campi non esatti\n\n");

        if (metrics.Errors.Count == 0)
        {
            report.Append("Nessuno.\n\n");

            return;
        }

        report.Append(Md.Table(
            ["Documento", "Campo", "Atteso", "Estratto", "Confidenza", "Esito"],
            metrics.Errors.Select(error => (IReadOnlyList<string>)
            [
                error.Document, Md.Code(error.Path), Md.Cell(error.Expected), Md.Cell(error.Extracted),
                error.Confidence is { } confidence ? Md.Number(confidence) : Md.None,
                error.Outcome is { } outcome ? Label(outcome) + (error.IsWrongProposal ? " ⚠" : string.Empty) : "non estratto"
            ])));
        report.Append('\n');
    }

    private static void AppendSensitivity(StringBuilder report, ExtractionEvaluation evaluation, ExtractionOptions options)
    {
        report.Append("## Sensibilità\n\n");
        report.Append("Le stesse letture giudicate cambiando una manopola alla volta. \"Da far vedere\" sono i campi che il golden set vuole davanti a una persona; \"giusti in revisione\" è il lavoro in più.\n\n");

        Append(report, "### Soglia di revisione", "Soglia", ExtractionSensitivity.ByThreshold(evaluation, options));
        Append(report, "### Una lettura o due", "Letture", ExtractionSensitivity.ByPasses(evaluation, options));
        report.Append($"Due letture costano circa il doppio del tempo e dei token di una ({Md.Seconds(evaluation.Run.ElapsedMs / Math.Max(evaluation.Documents.Count, 1))} a documento con due).\n\n");
        Append(report, "### Peso del segnale `Annotation`", "Peso", ExtractionSensitivity.ByAnnotationWeight(evaluation, options));

        static void Append(StringBuilder report, string title, string firstColumn, IReadOnlyList<SensitivityRow> rows)
        {
            report.Append(title).Append("\n\n");
            report.Append(Md.Table(
                [firstColumn, "Proposte", "Proposte sbagliate", "In revisione", "Da far vedere, in revisione", "Lavoro in più"],
                rows.Select(row => (IReadOnlyList<string>)
                [
                    row.Label, $"{row.Proposals}", $"{row.WrongProposals}", $"{row.InReview}",
                    $"{row.MustReviewInReview} su {row.MustReviewTotal}", $"{row.ExactInReview}"
                ])));
            report.Append('\n');
        }
    }

    private static void AppendDocuments(StringBuilder report, ExtractionEvaluation evaluation, ExtractionMetrics metrics)
    {
        Dictionary<string, AccuracyRow> rows = metrics.ByDocument.ToDictionary(r => r.Name, StringComparer.Ordinal);
        Dictionary<string, DocumentReading> readings = evaluation.Run.Readings.ToDictionary(r => r.Code, StringComparer.Ordinal);

        report.Append("## Per documento\n\n");
        report.Append(Md.Table(
            ["Documento", "Categoria", "Attesi", "Esatti", "Errati", "Mancanti", "In più", "Proposte", "In revisione", "Tempo", "Token"],
            evaluation.Documents.Select(document =>
            {
                string code = document.Golden.Code;
                AccuracyRow row = rows.GetValueOrDefault(code) ?? new AccuracyRow(code, 0, 0, 0, 0, 0);
                DocumentReading? reading = readings.GetValueOrDefault(code);
                FieldScore[] scores = [.. metrics.Scores.Where(s => s.Document == code)];

                return (IReadOnlyList<string>)
                [
                    code, document.Golden.Category, $"{row.Expected}", $"{row.Exact}", $"{row.Wrong}", $"{row.Missing}", $"{row.Extra}",
                    $"{scores.Count(s => s.Outcome == ExtractionFieldOutcome.Proposed)}", $"{scores.Count(s => s.Outcome == ExtractionFieldOutcome.InReview)}",
                    reading is null ? Md.None : Md.Seconds(reading.ElapsedMs),
                    reading is null ? Md.None : $"{Md.Number(reading.InputTokens)} / {Md.Number(reading.OutputTokens)}"
                ];
            })));

        string[] failed = [.. evaluation.Run.Readings.Where(r => r.First is null).Select(r => $"`{r.Code}`: {r.Error ?? (r.Refused ? "rifiuto del modello" : "nessuna estrazione valida")}")];

        if (failed.Length > 0)
        {
            report.Append("\nDocumenti senza estrazione: ").AppendJoin("; ", failed).Append(".\n");
        }
    }

    public static string Label(ExtractionFieldOutcome outcome) => outcome switch
    {
        ExtractionFieldOutcome.Matches => "coincide con il TMS",
        ExtractionFieldOutcome.Proposed => "proposta",
        ExtractionFieldOutcome.InReview => "revisione",
        _ => "non applicabile"
    };
}
