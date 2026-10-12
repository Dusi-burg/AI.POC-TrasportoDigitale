using System.Text;
using Dusiburg.AI.Trasporto.Efti.Core.Import;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Eval.Reports;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;

namespace Dusiburg.AI.Trasporto.Eval.Validation;

/// <summary>Esito di un caso del golden set: le regole attese e quelle scattate.</summary>
public sealed record ValidationCaseResult(string Name, IReadOnlyList<string> Expected, IReadOnlyList<string> Fired)
{
    public bool Passed => Expected.Order(StringComparer.Ordinal).SequenceEqual(Fired.Order(StringComparer.Ordinal));
}

/// <summary>Quanti ordini del seed hanno un difetto voluto e in quanti le sue regole sono scattate.</summary>
public sealed record SeedDefectResult(SeedDefect Defect, IReadOnlyList<string> Rules, int Orders, int Recognized);

/// <summary>
/// <c>eval validation</c>: il golden set della validazione e i difetti voluti del seed. È codice deterministico: il report
/// documenta un 100% che i test già impongono, e mostra a chi legge quali regole scattano su quali casi.
/// </summary>
public static class ValidationCommand
{
    private static readonly DateTimeOffset ImportedAt = new(DemoCmrs.ReferenceDay.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

    public static int Run(EvalSettings settings, string outputDirectory, TextWriter log)
    {
        EftiValidator validator = EftiValidator.CreateDefault();
        IReadOnlyList<ValidationCaseResult> cases = RunGolden(validator, Path.Combine(settings.DataDirectory, GoldenValidationSet.FileName));
        string path = Md.Write(outputDirectory, Md.ReportFile(DateTimeOffset.Now, "validation"), Render(validator, cases));

        log.WriteLine($"Golden set della validazione: {Md.Share(cases.Count(c => c.Passed), cases.Count)}.");
        log.WriteLine($"Report: {path}");

        return cases.All(c => c.Passed) ? 0 : 2;
    }

    public static IReadOnlyList<ValidationCaseResult> RunGolden(EftiValidator validator, string goldenFile) =>
    [
        .. GoldenValidationSet.Load(goldenFile).Select(goldenCase => new ValidationCaseResult(
            goldenCase.Name,
            goldenCase.Expected,
            [.. validator.Evaluate(ShipmentFactory.Create(goldenCase.Order, goldenCase.Carrier, ImportedAt)).Findings.Select(f => f.RuleCode).Distinct()]))
    ];

    /// <summary>Per ogni difetto voluto del seed, in quanti ordini tutte le sue regole sono scattate.</summary>
    public static IReadOnlyList<SeedDefectResult> RunSeedDefects(EftiValidator validator)
    {
        Dictionary<string, HashSet<string>> firedByOrder = GoldenCmrs.Orders.ToDictionary(
            order => order.Key,
            order => validator.Evaluate(GoldenCmrs.ShipmentFor(order.Key)).Findings.Select(f => f.RuleCode).ToHashSet(StringComparer.Ordinal));

        return
        [
            .. GoldenValidationSet.RulesByDefect.Select(entry =>
            {
                string[] orders = [.. GoldenCmrs.Seed.DefectsByOrderCode.Where(d => d.Value.Contains(entry.Key) && firedByOrder.ContainsKey(d.Key)).Select(d => d.Key)];

                return new SeedDefectResult(entry.Key, entry.Value, orders.Length, orders.Count(order => entry.Value.All(firedByOrder[order].Contains)));
            })
        ];
    }

    public static string Render(EftiValidator validator, IReadOnlyList<ValidationCaseResult> cases)
    {
        var report = new StringBuilder();
        IReadOnlyList<SeedDefectResult> defects = RunSeedDefects(validator);
        Shipment[] shipments = [.. GoldenCmrs.Orders.Keys.Select(GoldenCmrs.ShipmentFor)];
        DatasetEvaluation[] evaluations = [.. shipments.Select(validator.Evaluate)];
        HashSet<string> demoOrders = [.. DemoCatalog.Orders.Select(o => o.Code)];

        report.Append($"# Valutazione della validazione eFTI — {Md.Now(DateTimeOffset.Now)}\n\n");
        report.Append("> Generato da `Eval -- eval validation`. Nessun modello: sono regole dichiarative, e l'esito atteso è il 100%.\n\n");
        report.Append("| Parametro | Valore |\n|---|---|\n");
        report.Append($"| Schema eFTI | {validator.SchemaVersion} |\n");
        report.Append($"| Regole dichiarative | {ValidationRules.All.Count} |\n");
        report.Append($"| Casi del golden set | {cases.Count} |\n");
        report.Append($"| Ordini del seed valutati | {shipments.Length} (seed {GoldenCmrs.Seed.RandomSeed}, giorno {DemoCmrs.ReferenceDay:yyyy-MM-dd}) |\n\n");

        report.Append("## Sintesi\n\n");
        report.Append("| Misura | Valore |\n|---|---|\n");
        report.Append($"| Casi del golden set con le regole attese, tutte e sole | **{Md.Share(cases.Count(c => c.Passed), cases.Count)}** |\n");
        report.Append($"| Difetti del seed riconosciuti | **{Md.Share(defects.Sum(d => d.Recognized), defects.Sum(d => d.Orders))}** |\n");
        report.Append($"| Spedizioni del seed valide / incomplete | {evaluations.Count(e => e.Status == DatasetStatus.Valid)} / {evaluations.Count(e => e.Status == DatasetStatus.Incomplete)} |\n");
        report.Append($"| Spedizioni senza difetti voluti ma incomplete | {shipments.Zip(evaluations).Count(p => !demoOrders.Contains(p.First.TmsOrderCode) && !GoldenCmrs.Seed.DefectsByOrderCode.ContainsKey(p.First.TmsOrderCode) && p.Second.Status == DatasetStatus.Incomplete)} |\n\n");

        report.Append("## Golden set\n\n");
        report.Append(Md.Table(
            ["Caso", "Regole attese", "Regole scattate", "Esito"],
            cases.Select(c => (IReadOnlyList<string>)
            [
                Md.Cell(c.Name), Codes(c.Expected), Codes(c.Fired), c.Passed ? "✓" : "**✗**"
            ])));

        report.Append("\n## Difetti voluti del seed\n\n");
        report.Append(Md.Table(
            ["Difetto", "Regole che devono scattare", "Ordini", "Riconosciuti"],
            defects.Select(d => (IReadOnlyList<string>)[$"`{d.Defect}`", Codes(d.Rules), $"{d.Orders}", Md.Share(d.Recognized, d.Orders)])));

        report.Append("\n## Regole scattate sul seed\n\n");
        report.Append(Md.Table(
            ["Regola", "Gravità", "Spedizioni"],
            evaluations
                .SelectMany(e => e.Findings.Select(f => (f.RuleCode, f.Severity)).Distinct())
                .GroupBy(f => f)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.RuleCode, StringComparer.Ordinal)
                .Select(g => (IReadOnlyList<string>)[$"`{g.Key.RuleCode}`", g.Key.Severity == IssueSeverity.Error ? "bloccante" : "avviso", $"{g.Count()}"])));

        return report.ToString();
    }

    private static string Codes(IReadOnlyList<string> codes) =>
        codes.Count == 0 ? "nessuna" : string.Join(", ", codes.Order(StringComparer.Ordinal).Select(code => $"`{code}`"));
}
