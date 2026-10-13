using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Eval.Extraction;
using Dusiburg.AI.Trasporto.Eval.Live;
using Dusiburg.AI.Trasporto.Eval.Reports;

namespace Dusiburg.AI.Trasporto.Eval.Bench;

/// <summary>Ciò che un modello ha fatto nelle tre prove; una prova non eseguita resta <c>null</c>.</summary>
public sealed record ModelBench(
    ModelCandidate Model, ExtractionEvaluation? Text, ExtractionEvaluation? Images, ExtractionEvaluation? TextOnImageDocuments,
    IReadOnlyList<MappingRunScore>? Mapping);

/// <summary>
/// <c>bench models</c>: gli stessi insiemi di prova letti da più modelli, locali e cloud. Qualità, latenza, token e costo per
/// documento (PLAN §5, Fase 9). Le letture si salvano: con <c>--reuse</c> un modello già misurato non si richiama.
/// </summary>
public static class BenchCommand
{
    public const string ExtractionPart = "extraction";
    public const string ImagesPart = "images";
    public const string MappingPart = "mapping";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<int> RunAsync(Cli cli, EvalSettings settings, string outputDirectory, TextWriter log, CancellationToken cancellationToken)
    {
        IReadOnlyList<ModelCandidate> models = cli.Models(ModelCandidate.Defaults);
        string[] parts = cli.Value("parts")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [ExtractionPart, ImagesPart, MappingPart];
        ExtractionOptions options = ExtractionCommand.OptionsFrom(cli);
        int repeat = cli.Int("repeat") ?? 3;
        bool reuse = cli.Has("reuse");
        string raw = Path.Combine(outputDirectory, ExtractionCommand.RawDirectory);
        IReadOnlyList<GoldenDocument> documents = GoldenDocument.Load(settings.DataDirectory, cli.Int("limit"));
        IReadOnlyList<GoldenDocument> imageDocuments = [.. documents.Where(d => CmrImageRenderer.Documents.Contains(d.Golden.Code))];
        MappingGolden mappingGolden = MappingGolden.Load(settings.DataDirectory);
        List<ModelBench> results = [];

        using var stack = new LiveStack(settings);

        if (parts.Contains(MappingPart))
        {
            await stack.EnsureAvailableAsync(cancellationToken);
        }

        foreach (ModelCandidate model in models)
        {
            ExtractionEvaluation? text = null;
            ExtractionEvaluation? images = null;
            IReadOnlyList<MappingRunScore>? mapping = null;

            if (parts.Contains(ExtractionPart))
            {
                log.WriteLine($"[{model}] estrazione da testo, {documents.Count} documenti...");

                ExtractionRunFile run = await LoadOrRunAsync(
                    Path.Combine(raw, $"extraction_{ExtractionRunFile.TextSource}_{model.FileName}.json"), reuse, log,
                    () => ExtractionRunner.RunAsync(settings, model, documents, options.PassesFor(model.Provider), log, cancellationToken));

                text = new ExtractionEvaluation(documents, run);
            }

            if (parts.Contains(ImagesPart))
            {
                log.WriteLine($"[{model}] estrazione da immagine, {imageDocuments.Count} documenti...");

                ExtractionRunFile run = await LoadOrRunAsync(
                    Path.Combine(raw, $"extraction_{ExtractionRunFile.ImageSource}_{model.FileName}.json"), reuse, log,
                    () => ImageExtractionRunner.RunAsync(settings, model, imageDocuments, log, cancellationToken));

                images = new ExtractionEvaluation(imageDocuments, run);
            }

            if (parts.Contains(MappingPart))
            {
                string file = Path.Combine(raw, $"mapping_{model.FileName}.json");

                log.WriteLine($"[{model}] mappatura, {mappingGolden.Cases.Count} spedizioni × {repeat}...");

                if (reuse && File.Exists(file))
                {
                    mapping = JsonSerializer.Deserialize<IReadOnlyList<MappingRunScore>>(await File.ReadAllTextAsync(file, cancellationToken), Json);
                    log.WriteLine($"  riuso {file}");
                }
                else
                {
                    mapping = await MappingBench.RunAsync(stack, model, mappingGolden, repeat, log, cancellationToken);
                    Directory.CreateDirectory(raw);
                    await File.WriteAllTextAsync(file, JsonSerializer.Serialize(mapping, Json).ReplaceLineEndings("\n") + "\n", cancellationToken);
                }
            }

            // Per confrontare immagine e testo sugli stessi documenti servono le letture da testo di quei documenti soltanto.
            ExtractionEvaluation? textOnImages = text is null ? null : new ExtractionEvaluation(imageDocuments, text.Run);

            results.Add(new ModelBench(model, text, images, textOnImages, mapping));
        }

        string path = Md.Write(outputDirectory, Md.ReportFile(DateTimeOffset.Now, "bench_models"), Render(results, options, settings.Prices, mappingGolden));

        log.WriteLine($"Report: {path}");

        return 0;
    }

    private static async Task<ExtractionRunFile> LoadOrRunAsync(string file, bool reuse, TextWriter log, Func<Task<ExtractionRunFile>> run)
    {
        if (reuse && File.Exists(file))
        {
            log.WriteLine($"  riuso {file}");

            return ExtractionRunFile.Read(file);
        }

        ExtractionRunFile result = await run();

        result.Save(Path.GetDirectoryName(file)!, Path.GetFileName(file));

        return result;
    }

    public static string Render(IReadOnlyList<ModelBench> results, ExtractionOptions options, ModelPrices prices, MappingGolden mappingGolden)
    {
        var report = new StringBuilder();

        report.Append($"# Confronto dei modelli: locale contro cloud — {Md.Now(DateTimeOffset.Now)}\n\n");
        report.Append("> Generato da `Eval -- bench models`. Stessi documenti, stesse istruzioni, stesso codice che giudica: cambia solo il modello.\n\n");
        report.Append("| Parametro | Valore |\n|---|---|\n");
        report.Append($"| Modelli | {string.Join(", ", results.Select(r => $"`{r.Model.Model}` ({(r.Model.IsLocal ? "locale, Ollama" : "cloud, API Anthropic")})"))} |\n");
        report.Append($"| Prezzi di listino | {string.Join("; ", results.Where(r => !r.Model.IsLocal).Select(r => prices.For(r.Model) is { } p ? $"`{r.Model.Model}` {Md.Number(p.InputPerMillion)} $ / {Md.Number(p.OutputPerMillion)} $ per milione di token in ingresso / in uscita" : $"`{r.Model.Model}` senza prezzo"))} (al {ModelPrices.DefaultsAsOf}) |\n");
        report.Append("| Modello locale | nessun costo a consumo: il costo è il tempo della macchina (hardware di riferimento: GPU con 8 GB di VRAM) |\n");
        report.Append($"| Soglia di revisione, letture | {Md.Number(options.ReviewThreshold)}; {string.Join(", ", results.Where(r => r.Text is not null).Select(r => $"`{r.Model.Model}` {r.Text!.Run.Passes}"))} per documento (una sola per le immagini) |\n\n");

        AppendText(report, results, prices, options);
        AppendImages(report, results, prices, options);
        AppendMapping(report, results, prices, mappingGolden);

        return report.ToString();
    }

    private static void AppendText(StringBuilder report, IReadOnlyList<ModelBench> results, ModelPrices prices, ExtractionOptions options)
    {
        (ModelBench Bench, ExtractionMetrics Metrics)[] rows = [.. results.Where(r => r.Text is not null).Select(r => (r, new ExtractionMetrics(r.Text!.Score(options))))];

        if (rows.Length == 0)
        {
            return;
        }

        report.Append($"## Estrazione dal testo del PDF\n\nGolden set di {rows[0].Bench.Text!.Documents.Count} CMR. È il percorso del prodotto: il modello legge il testo, il codice calcola la confidenza.\n\n");
        report.Append(Md.Table(
            ["Modello", "Accuratezza per campo", "Errati / mancanti / in più", "Errori sopra soglia", "Da far vedere, in revisione", "Valori iniettati usciti", "Tempo a documento", "Token a documento (in / out)", "Costo a documento", "Costo per 1.000 documenti"],
            rows.Select(row =>
            {
                ExtractionRunFile run = row.Bench.Text!.Run;
                int documents = Math.Max(row.Bench.Text.Documents.Count, 1);
                decimal? cost = prices.Cost(run.Candidate, run.InputTokens, run.OutputTokens);

                return (IReadOnlyList<string>)
                [
                    $"`{run.Model}`", $"**{Md.Percent(row.Metrics.Overall.Exact, row.Metrics.Overall.Expected)}** ({row.Metrics.Overall.Exact} su {row.Metrics.Overall.Expected})",
                    $"{row.Metrics.Overall.Wrong} / {row.Metrics.Overall.Missing} / {row.Metrics.Overall.Extra}", $"**{row.Metrics.WrongProposals.Count}**",
                    $"{row.Metrics.MustReview.InReview} su {row.Metrics.MustReview.Total}", $"{row.Metrics.InjectedValues.Count}",
                    Md.Seconds(run.ElapsedMs / documents), $"{Md.Number(run.InputTokens / documents)} / {Md.Number(run.OutputTokens / documents)}",
                    run.Candidate.IsLocal ? "0 $" : Md.Usd(cost / documents), run.Candidate.IsLocal ? "0 $" : cost is null ? Md.None : $"{Md.Number(cost.Value / documents * 1000m)} $"
                ];
            })));

        string[] categories = [.. rows[0].Metrics.ByCategory.Select(c => c.Name)];

        report.Append("\n### Accuratezza per categoria di documento\n\n");
        report.Append(Md.Table(
            ["Categoria", .. rows.Select(r => $"`{r.Bench.Model.Model}`")],
            categories.Select(category => (IReadOnlyList<string>)
            [
                category, .. rows.Select(r => r.Metrics.ByCategory.FirstOrDefault(c => c.Name == category) is { } row ? Md.Percent(row.Exact, row.Expected) : Md.None)
            ])));

        report.Append("\n### Accuratezza per gruppo di campi\n\n");
        report.Append(Md.Table(
            ["Gruppo", .. rows.Select(r => $"`{r.Bench.Model.Model}`")],
            FieldGroups.All.Select(group => (IReadOnlyList<string>)
            [
                group, .. rows.Select(r => r.Metrics.ByGroup.FirstOrDefault(g => g.Name == group) is { } row ? Md.Percent(row.Exact, row.Expected) : Md.None)
            ])));
        report.Append('\n');
    }

    private static void AppendImages(StringBuilder report, IReadOnlyList<ModelBench> results, ModelPrices prices, ExtractionOptions options)
    {
        ModelBench[] rows = [.. results.Where(r => r.Images is not null)];

        if (rows.Length == 0)
        {
            return;
        }

        report.Append($"## Estrazione da una scansione\n\n{rows[0].Images!.Documents.Count} CMR resi come immagine \"sporca\" (120 dpi, ruotata, sfocata, con rumore, JPEG). ");
        report.Append("Non è una funzione del prodotto, che rifiuta i PDF senza testo: serve a vedere dove un modello non arriva. Qui non c'è un testo con cui riscontrare i valori, quindi nessuna confidenza: solo l'accuratezza.\n\n");
        report.Append(Md.Table(
            ["Modello", "Accuratezza da immagine", "Errati / mancanti / in più", "Sugli stessi documenti, dal testo", "Documenti non letti", "Tempo a documento", "Token a documento (in / out)", "Costo a documento"],
            rows.Select(row =>
            {
                ExtractionRunFile run = row.Images!.Run;
                var metrics = new ExtractionMetrics(row.Images.Score(options));
                ExtractionMetrics? fromText = row.TextOnImageDocuments is null ? null : new ExtractionMetrics(row.TextOnImageDocuments.Score(options));
                int documents = Math.Max(row.Images.Documents.Count, 1);

                return (IReadOnlyList<string>)
                [
                    $"`{run.Model}`", $"**{Md.Percent(metrics.Overall.Exact, metrics.Overall.Expected)}** ({metrics.Overall.Exact} su {metrics.Overall.Expected})",
                    $"{metrics.Overall.Wrong} / {metrics.Overall.Missing} / {metrics.Overall.Extra}",
                    fromText is null ? Md.None : Md.Percent(fromText.Overall.Exact, fromText.Overall.Expected),
                    $"{run.Readings.Count(r => r.First is null)}", Md.Seconds(run.ElapsedMs / documents),
                    $"{Md.Number(run.InputTokens / documents)} / {Md.Number(run.OutputTokens / documents)}",
                    run.Candidate.IsLocal ? "0 $" : Md.Usd(prices.Cost(run.Candidate, run.InputTokens, run.OutputTokens) / documents)
                ];
            })));

        report.Append("\n### Da immagine, per gruppo di campi\n\n");
        report.Append(Md.Table(
            ["Gruppo", .. rows.Select(r => $"`{r.Model.Model}`")],
            FieldGroups.All.Select(group => (IReadOnlyList<string>)
            [
                group, .. rows.Select(r => new ExtractionMetrics(r.Images!.Score(options)).ByGroup.FirstOrDefault(g => g.Name == group) is { } row
                    ? Md.Percent(row.Exact, row.Expected)
                    : Md.None)
            ])));

        string[] failures = [.. rows.SelectMany(r => r.Images!.Run.Readings.Where(x => x.First is null).Select(x => $"`{r.Model.Model}` su `{x.Code}`: {Md.Cell(x.Error ?? "nessuna estrazione valida")}"))];

        if (failures.Length > 0)
        {
            report.Append("\nDocumenti non letti: ").AppendJoin("; ", failures).Append(".\n");
        }

        report.Append('\n');
    }

    private static void AppendMapping(StringBuilder report, IReadOnlyList<ModelBench> results, ModelPrices prices, MappingGolden golden)
    {
        ModelBench[] rows = [.. results.Where(r => r.Mapping is { Count: > 0 })];

        if (rows.Length == 0)
        {
            return;
        }

        report.Append("## Agente di mappatura\n\n");
        report.Append($"{golden.Cases.Count} spedizioni incomplete, {rows[0].Mapping!.Count / Math.Max(golden.Cases.Count, 1)} ripetizioni ciascuna, con l'agente vero e i tool di `efti-mcp`. ");
        report.Append("Nella prima l'ordine del TMS contiene i dati mancanti; nelle altre no, e l'esito giusto è non proporre nulla. Una proposta \"non attesa\" è un valore che il controllo sulla fonte ha lasciato passare perché è scritto nell'ordine, ma per un altro campo.\n\n");
        report.Append(Md.Table(
            ["Modello", "Run riusciti", "Proposte attese ottenute", "Proposte non attese", "Scartate dal codice", "Chiamate ai tool a run", "Tempo a run", "Token a run (in / out)", "Costo a run"],
            rows.Select(row =>
            {
                IReadOnlyList<MappingRunScore> runs = row.Mapping!;
                long? input = runs.Any(r => r.InputTokens is not null) ? runs.Sum(r => r.InputTokens ?? 0) : null;
                long? output = runs.Any(r => r.OutputTokens is not null) ? runs.Sum(r => r.OutputTokens ?? 0) : null;

                return (IReadOnlyList<string>)
                [
                    $"`{row.Model.Model}`", Md.Share(runs.Count(r => r.Passed), runs.Count), Md.Share(runs.Sum(r => r.ExpectedObtained), runs.Sum(r => r.Expected)),
                    $"{runs.Sum(r => r.Unexpected.Count)}", $"{runs.Sum(r => r.Discarded)}", Md.Number((decimal)runs.Average(r => r.ToolCalls), "0.0"),
                    Md.Seconds(runs.Average(r => r.ElapsedMs)), $"{Md.Number(input / runs.Count)} / {Md.Number(output / runs.Count)}",
                    row.Model.IsLocal ? "0 $" : Md.Usd(prices.Cost(row.Model, input, output) / runs.Count)
                ];
            })));

        report.Append("\n### Per spedizione\n\n");
        report.Append(Md.Table(
            ["Spedizione", "Caso", .. rows.Select(r => $"`{r.Model.Model}`")],
            golden.Cases.Select(mappingCase => (IReadOnlyList<string>)
            [
                mappingCase.ShipmentCode, Md.Cell(mappingCase.Title),
                .. rows.Select(row =>
                {
                    MappingRunScore[] runs = [.. row.Mapping!.Where(r => r.ShipmentCode == mappingCase.ShipmentCode)];

                    return runs.Length == 0
                        ? Md.None
                        : $"{runs.Count(r => r.Passed)} su {runs.Length} riusciti; attese {runs.Sum(r => r.ExpectedObtained)}/{runs.Sum(r => r.Expected)}, non attese {runs.Sum(r => r.Unexpected.Count)}"
                          + (runs.Any(r => r.Error is not null) ? $", errori {runs.Count(r => r.Error is not null)}" : string.Empty);
                })
            ])));

        string[] unexpected = [.. rows.SelectMany(row => row.Mapping!.SelectMany(run => run.Unexpected.Select(u => $"`{row.Model.Model}` su {run.ShipmentCode}: {Md.Code(u)}"))).Distinct()];

        if (unexpected.Length > 0)
        {
            report.Append("\nProposte non attese: ").AppendJoin("; ", unexpected).Append(".\n");
        }

        string[] errors = [.. rows.SelectMany(row => row.Mapping!.Where(run => run.Error is not null).Select(run => $"`{row.Model.Model}` su {run.ShipmentCode}: {Md.Cell(run.Error)}")).Distinct()];

        if (errors.Length > 0)
        {
            report.Append("\nRun con errore: ").AppendJoin("; ", errors).Append(".\n");
        }
    }
}
