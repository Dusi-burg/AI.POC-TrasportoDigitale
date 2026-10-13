using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Eval.Reports;

namespace Dusiburg.AI.Trasporto.Eval.Extraction;

/// <summary>
/// <c>eval extraction</c>: fa leggere il golden set dei CMR a un modello, salva le letture e scrive il report. Con
/// <c>--replay &lt;file&gt;</c> ricalcola il report da letture già salvate, senza chiamare nessun modello.
/// </summary>
public static class ExtractionCommand
{
    /// <summary>Cartella delle letture grezze, sotto quella dei report.</summary>
    public const string RawDirectory = "raw";

    public static async Task<int> RunAsync(Cli cli, EvalSettings settings, string outputDirectory, TextWriter log, CancellationToken cancellationToken)
    {
        IReadOnlyList<GoldenDocument> documents = GoldenDocument.Load(settings.DataDirectory, cli.Int("limit"), cli.Value("category"));
        ExtractionOptions options = OptionsFrom(cli);
        ExtractionRunFile run;

        if (cli.Value("replay") is { } replay)
        {
            run = ExtractionRunFile.Read(replay);
            log.WriteLine($"Letture da {replay}: {run.Model}, {run.Readings.Count} documenti.");
        }
        else
        {
            ModelCandidate model = cli.Model(settings.DefaultModel) ?? settings.DefaultModel;

            log.WriteLine($"Estrazione con {model} su {documents.Count} documenti, {options.PassesFor(model.Provider)} letture ciascuno...");
            run = await ExtractionRunner.RunAsync(settings, model, documents, options.PassesFor(model.Provider), log, cancellationToken);
            log.WriteLine($"Letture salvate in {run.Save(Path.Combine(outputDirectory, RawDirectory), RawFile(run))}");
        }

        // Si giudicano i documenti che hanno una lettura: con --limit o --category il report riguarda solo quelli.
        var evaluation = new ExtractionEvaluation([.. documents.Where(d => run.Readings.Any(r => r.Code == d.Golden.Code))], run);
        string report = ExtractionReport.Render(evaluation, options, settings.Prices);
        string path = Md.Write(outputDirectory, Md.ReportFile(DateTimeOffset.Now, $"extraction_{run.Candidate.FileName}"), report);
        var metrics = new ExtractionMetrics(evaluation.Score(options));

        log.WriteLine(
            $"Accuratezza {Md.Share(metrics.Overall.Exact, metrics.Overall.Expected)}; errori sopra soglia {metrics.WrongProposals.Count}; " +
            $"in revisione {metrics.InReview}; valori iniettati usciti {metrics.InjectedValues.Count}.");
        log.WriteLine($"Report: {path}");

        return 0;
    }

    public static string RawFile(ExtractionRunFile run) => $"extraction_{run.Source}_{run.Candidate.FileName}.json";

    /// <summary>Pesi e soglia di default del codice (quelli confermati al CHECKPOINT 7), salvo <c>--threshold</c> e <c>--passes</c>.</summary>
    public static ExtractionOptions OptionsFrom(Cli cli)
    {
        var options = new ExtractionOptions();

        options.ReviewThreshold = cli.Decimal("threshold") ?? options.ReviewThreshold;
        // Senza --passes le letture sono quelle del provider del modello (due in locale, una sul cloud); con --passes valgono
        // per qualunque modello, per esempio per rimisurare due letture anche sul cloud.
        if (cli.Int("passes") is { } passes)
        {
            options.Passes = passes;
            options.PassesByProvider.Clear();
        }

        return options.IsValid() ? options : throw new ArgumentException("Opzioni dell'estrazione non valide: soglia fra 0 e 1, letture fra 1 e 3.");
    }
}
