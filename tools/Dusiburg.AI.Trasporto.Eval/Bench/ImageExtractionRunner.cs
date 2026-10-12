using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Documents.Extraction;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Eval.Extraction;
using Dusiburg.AI.Trasporto.Eval.Reports;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Eval.Bench;

/// <summary>
/// Fa leggere a un modello la scansione di un CMR invece del suo testo, con le stesse istruzioni e lo stesso schema
/// dell'estrattore. Esiste solo qui: nei servizi un documento senza testo estraibile è rifiutato (PLAN §1, non-obiettivi).
/// </summary>
public static class ImageExtractionRunner
{
    private const string Request = "Extract the consignment note from the attached scan.";

    private const string ScanNote = "\nHere the document is not given as text: it is the attached image of a scanned consignment note. Read it as carefully as the text.";

    public static async Task<ExtractionRunFile> RunAsync(
        EvalSettings settings, ModelCandidate model, IReadOnlyList<GoldenDocument> documents, TextWriter log, CancellationToken cancellationToken)
    {
        using ChatModel chat = settings.ModelsFor(model).Create(ModelTask.Extraction);
        long started = Stopwatch.GetTimestamp();
        List<DocumentReading> readings = [];

        foreach (GoldenDocument document in documents)
        {
            byte[] image = await File.ReadAllBytesAsync(Path.Combine(settings.DataDirectory, CmrImageRenderer.FileOf(document.Golden.Code)), cancellationToken);
            DocumentReading reading = await ReadAsync(chat, document.Golden.Code, image, cancellationToken);

            readings.Add(reading);
            log.WriteLine($"  {document.Golden.Code,-14} {Md.Seconds(reading.ElapsedMs),8}  {reading.Error ?? (reading.First is null ? "nessuna estrazione valida" : $"token {reading.InputTokens}/{reading.OutputTokens}")}");
        }

        return new ExtractionRunFile(
            Md.Now(DateTimeOffset.Now), model.Provider, model.Model, ExtractionRunFile.ImageSource, 1, Stopwatch.GetElapsedTime(started).TotalMilliseconds, readings);
    }

    private static async Task<DocumentReading> ReadAsync(ChatModel chat, string code, byte[] image, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        try
        {
            ChatOptions options = chat.DefaultOptions.Clone();

            options.Instructions = CmrExtractor.Instructions + ScanNote;

            ChatResponse<CmrExtraction> response = await chat.ChatClient.GetResponseAsync<CmrExtraction>(
                [new ChatMessage(ChatRole.User, [new TextContent(Request), new DataContent(image, CmrImageRenderer.MediaType)])],
                TrasportoJson.Options, options, CmrExtractor.UsesNativeSchema(chat.Provider), cancellationToken);

            // Una risposta che non rispetta lo schema lascia il documento senza estrazione.
            CmrExtraction? extraction = CmrExtractor.TryRead(response, out CmrExtraction? result) ? result : null;

            return new DocumentReading(
                code, Stopwatch.GetElapsedTime(started).TotalMilliseconds, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, extraction, null, 1,
                response.FinishReason == ChatFinishReason.ContentFilter, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DocumentReading(
                code, Stopwatch.GetElapsedTime(started).TotalMilliseconds, null, null, null, null, 1, false, $"{exception.GetType().Name}: {exception.Message}");
        }
    }
}
