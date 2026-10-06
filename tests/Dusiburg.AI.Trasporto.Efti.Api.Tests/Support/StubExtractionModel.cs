using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;

/// <summary>
/// Il modello di estrazione dei test: a ogni lettura restituisce ciò che il test ha deciso. Di default "legge bene", cioè
/// restituisce la verità di riferimento del CMR; un test può fargli restituire altro per simulare un errore, un disaccordo fra
/// le due letture o un'iniezione riuscita. Nessuna chiamata reale.
/// </summary>
internal sealed class StubExtractionModel : IChatClientFactory, IChatClient
{
    private readonly Queue<string> _replies = new();

    /// <summary>Ciò che il modello ha ricevuto a ogni lettura.</summary>
    public List<(string? Instructions, string Message)> Requests { get; } = [];

    /// <summary>Le prossime risposte, una per lettura; finite quelle, il modello ripete l'ultima.</summary>
    public void Reply(params CmrExtraction[] readings) => ReplyRaw([.. readings.Select(r => JsonSerializer.Serialize(r, TrasportoJson.Options))]);

    public void ReplyRaw(params string[] replies)
    {
        _replies.Clear();
        Requests.Clear();

        foreach (string reply in replies)
        {
            _replies.Enqueue(reply);
        }
    }

    public ChatModel Create(ModelTask task) => new(this, task, "stub", "scripted", new ChatOptions());

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add((options?.Instructions, messages.Last().Text));

        string reply = _replies.Count > 1 ? _replies.Dequeue() : _replies.Peek();

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            Usage = new UsageDetails { InputTokenCount = 1_000, OutputTokenCount = 300 }
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (ChatResponseUpdate update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    // Il client è condiviso da tutta la fixture: ChatModel lo chiude a ogni estrazione, e non deve succedere nulla.
    public void Dispose()
    {
    }
}

/// <summary>I CMR sintetici della demo come li usa un test: il PDF del repository e la lettura corretta.</summary>
internal static class DemoCmrFiles
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "data", "cmr");

    public static byte[] Pdf(string code) => File.ReadAllBytes(Path.Combine(Directory, code + CmrGenerator.PdfExtension));

    /// <summary>Il modulo multipart con cui si carica un documento.</summary>
    public static MultipartFormDataContent Form(string fileName, byte[] content)
    {
        var file = new ByteArrayContent(content);

        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    /// <summary>
    /// Ciò che un modello che legge bene restituirebbe: la verità di riferimento del documento, riportata nella forma
    /// dell'estrazione. I pesi tornano come sono stampati (all'italiana), perché così li copia il modello.
    /// </summary>
    public static CmrExtraction CorrectReading(string code)
    {
        IReadOnlyDictionary<string, string> truth = DemoCmrs.Get(code).Expected;

        string? Value(string path) => truth.GetValueOrDefault(path);

        CmrParty Party(string element) => new(
            Value($"{element}/name"), Value($"{element}/postalAddress/streetName"), Value($"{element}/postalAddress/postcode"),
            Value($"{element}/postalAddress/cityName"), Value($"{element}/postalAddress/countryCode"), Value($"{element}/taxRegistration/id"));

        CmrPlace Place(string element) => new(
            Value($"{element}/postalAddress/streetName"), Value($"{element}/postalAddress/postcode"),
            Value($"{element}/postalAddress/cityName"), Value($"{element}/postalAddress/countryCode"));

        List<CmrGoods> goods = [];

        for (int index = 0; Value($"transportPackage[{index}]/itemQuantity") is { } packages; index++)
        {
            string dangerousGoods = $"includedConsignmentItem[{index}]/transportDangerousGoods[0]";

            goods.Add(new CmrGoods(
                null, int.Parse(packages, System.Globalization.CultureInfo.InvariantCulture), null,
                Value($"{dangerousGoods}/uNDGID"), Value($"{dangerousGoods}/properShippingName"),
                Value($"{dangerousGoods}/hazardClassificationID"), Value($"{dangerousGoods}/packagingDangerLevelCode")));
        }

        return new CmrExtraction(
            Value("transportContractDocument/id"), Party("consignor"), Party("consignee"), Place("consigneeReceiptLocation"), Place("carrierAcceptanceLocation"),
            goods, int.Parse(truth["numberOfPackages"], System.Globalization.CultureInfo.InvariantCulture),
            Italian(truth["grossWeight"]), Value("mainCarriageTransportMovement[0]/usedTransportMeans/id"), Value("usedTransportEquipment[0]/id"));
    }

    /// <summary>8840 → "8.840": come il peso è stampato sul CMR.</summary>
    public static string Italian(string plainNumber) =>
        decimal.Parse(plainNumber, System.Globalization.CultureInfo.InvariantCulture).ToString("#,##0.##", System.Globalization.CultureInfo.GetCultureInfo("it-IT"));
}
