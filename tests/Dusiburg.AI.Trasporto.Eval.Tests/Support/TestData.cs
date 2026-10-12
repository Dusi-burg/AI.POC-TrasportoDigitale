using Dusiburg.AI.Trasporto.Eval.Extraction;

namespace Dusiburg.AI.Trasporto.Eval.Tests.Support;

/// <summary>I dati di <c>data/</c> copiati accanto ai test, e letture costruite a mano al posto di quelle di un modello.</summary>
internal static class TestData
{
    public static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Il golden set dei CMR con il testo dei PDF, letto una volta.</summary>
    public static IReadOnlyList<GoldenDocument> Documents { get; } = GoldenDocument.Load(Directory);

    public static GoldenDocument Document(string code) => Documents.Single(d => d.Golden.Code == code);

    /// <summary>Le letture di un lettore perfetto: per ogni documento, due volte la verità di riferimento.</summary>
    public static ExtractionRunFile PerfectRun(IReadOnlyList<GoldenDocument>? documents = null) => Run(
        documents ?? Documents, document => CmrExtractionBuilder.FromFields(document.Golden.Fields));

    public static ExtractionRunFile Run(IReadOnlyList<GoldenDocument> documents, Func<GoldenDocument, Shared.Contracts.Documents.CmrExtraction?> read) => new(
        "2026-10-12 10:00", "ollama", "modello-di-prova", ExtractionRunFile.TextSource, 2, 60_000,
        [.. documents.Select(document => read(document) is { } reading
            ? new DocumentReading(document.Golden.Code, 2_000, 1_800, 900, reading, reading, 2, false, null)
            : new DocumentReading(document.Golden.Code, 2_000, null, null, null, null, 2, false, "nessuna estrazione valida"))]);
}
