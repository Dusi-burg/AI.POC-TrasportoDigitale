using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dusiburg.AI.Trasporto.Documents.Extraction;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dusiburg.AI.Trasporto.Eval.Extraction;

/// <summary>Un documento del golden set pronto per essere letto: verità di riferimento e testo del PDF.</summary>
public sealed record GoldenDocument(GoldenCmr Golden, PdfText Pdf)
{
    /// <summary>La spedizione con cui il documento si confronta: nuova a ogni chiamata.</summary>
    public Shipment Shipment() => GoldenCmrs.ShipmentFor(Golden.OrderCode);

    public static IReadOnlyList<GoldenDocument> Load(string dataDirectory, int? limit = null, string? category = null) =>
    [
        .. CmrGenerator.ReadGolden(dataDirectory).Documents
            .Where(document => category is null || document.Category == category)
            .Take(limit ?? int.MaxValue)
            .Select(document => new GoldenDocument(document, PdfTextReader.Read(File.ReadAllBytes(Path.Combine(dataDirectory, document.File)))))
    ];
}

/// <summary>Ciò che il modello ha restituito per un documento: le letture grezze, senza giudizio.</summary>
/// <param name="First">La lettura che conta; <c>null</c> se il modello non ha dato un'estrazione valida.</param>
/// <param name="Second">La seconda lettura, che serve solo al segnale di accordo.</param>
/// <param name="Error">Errore della chiamata, se non è arrivata una risposta.</param>
public sealed record DocumentReading(
    string Code, double ElapsedMs, long? InputTokens, long? OutputTokens, CmrExtraction? First, CmrExtraction? Second, int Attempts, bool Refused, string? Error);

/// <summary>Le letture di un modello su tutto il golden set: si salvano per ricalcolare i report senza richiamare il modello.</summary>
/// <param name="Source"><c>text</c> se il modello ha letto il testo del PDF, <c>image</c> se ha letto un'immagine.</param>
public sealed record ExtractionRunFile(
    string CreatedAt, string Provider, string Model, string Source, int Passes, double ElapsedMs, IReadOnlyList<DocumentReading> Readings)
{
    public const string TextSource = "text";

    public const string ImageSource = "image";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonIgnore]
    public ModelCandidate Candidate => new(Provider, Model);

    [JsonIgnore]
    public long? InputTokens => Readings.Any(r => r.InputTokens is not null) ? Readings.Sum(r => r.InputTokens ?? 0) : null;

    [JsonIgnore]
    public long? OutputTokens => Readings.Any(r => r.OutputTokens is not null) ? Readings.Sum(r => r.OutputTokens ?? 0) : null;

    public string Save(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, fileName);

        File.WriteAllText(path, JsonSerializer.Serialize(this, Json).ReplaceLineEndings("\n") + "\n");

        return path;
    }

    public static ExtractionRunFile Read(string path) =>
        JsonSerializer.Deserialize<ExtractionRunFile>(File.ReadAllText(path), Json)
        ?? throw new InvalidOperationException($"Letture non leggibili: {path}");
}

/// <summary>Fa leggere i documenti al modello con l'estrattore vero, uno alla volta.</summary>
public static class ExtractionRunner
{
    public static async Task<ExtractionRunFile> RunAsync(
        EvalSettings settings, ModelCandidate model, IReadOnlyList<GoldenDocument> documents, int passes, TextWriter log, CancellationToken cancellationToken)
    {
        var extractor = new CmrExtractor(settings.ModelsFor(model), NullLogger<CmrExtractor>.Instance);
        long started = Stopwatch.GetTimestamp();
        List<DocumentReading> readings = [];

        foreach (GoldenDocument document in documents)
        {
            DocumentReading reading = await ReadAsync(extractor, document, passes, cancellationToken);

            readings.Add(reading);
            log.WriteLine(
                $"  {document.Golden.Code,-14} {Reports.Md.Seconds(reading.ElapsedMs),8}  " +
                (reading.Error ?? (reading.First is null ? "nessuna estrazione valida" : $"token {reading.InputTokens}/{reading.OutputTokens}")));
        }

        return new ExtractionRunFile(
            Reports.Md.Now(DateTimeOffset.Now), model.Provider, model.Model, ExtractionRunFile.TextSource, passes,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds, readings);
    }

    private static async Task<DocumentReading> ReadAsync(CmrExtractor extractor, GoldenDocument document, int passes, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        try
        {
            CmrExtractionRun run = await extractor.ExtractAsync(document.Pdf.Text, passes, cancellationToken);

            return new DocumentReading(
                document.Golden.Code, run.ElapsedMs, run.InputTokens, run.OutputTokens, run.Primary,
                run.Passes.Count > 1 ? run.Passes[1].Extraction : null, run.Passes.Sum(p => p.Attempts), run.Passes.Any(p => p.Refused), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Un documento che il modello non riesce a leggere è un risultato della misura, non un motivo per fermarla.
            return new DocumentReading(
                document.Golden.Code, Stopwatch.GetElapsedTime(started).TotalMilliseconds, null, null, null, null, 0, false,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }
}
