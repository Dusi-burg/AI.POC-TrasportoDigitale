using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Ai;

/// <summary>
/// Mascheramento dei dati personali prima che una richiesta parta verso un modello cloud (PLAN §4): email e numeri di telefono
/// nei messaggi e nei risultati dei tool diventano segnaposto. Si applica alla copia inviata al modello: il codice che controlla
/// i valori continua a vedere i dati veri. I nomi di persone fisiche non si riconoscono in modo affidabile e non vengono toccati.
/// </summary>
public sealed partial class PiiMaskingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public const string EmailPlaceholder = "[email]";

    public const string PhonePlaceholder = "[telefono]";

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages.Select(Mask), options, cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(messages.Select(Mask), options, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Email ovunque; telefoni solo se scritti in forma internazionale (<c>+39 …</c>) o introdotti da una parola che li annuncia:
    /// una sequenza di cifre qualunque può essere un CAP, un numero ONU o una partita IVA, e al modello servono.
    /// </summary>
    public static string Mask(string text)
    {
        string masked = EmailPattern().Replace(text, EmailPlaceholder);

        masked = LabelledPhonePattern().Replace(masked, match => match.Groups["label"].Value + PhonePlaceholder);

        return InternationalPhonePattern().Replace(masked, PhonePlaceholder);
    }

    /// <summary>Il messaggio originale non si modifica: resta nella conversazione dell'agente con i dati veri.</summary>
    private static ChatMessage Mask(ChatMessage message)
    {
        List<AIContent> contents = [.. message.Contents.Select(Mask)];

        return contents.SequenceEqual(message.Contents)
            ? message
            : new ChatMessage(message.Role, contents) { AuthorName = message.AuthorName, MessageId = message.MessageId, AdditionalProperties = message.AdditionalProperties };
    }

    private static AIContent Mask(AIContent content)
    {
        switch (content)
        {
            case TextContent text when Mask(text.Text) is { } masked && masked != text.Text:
                return new TextContent(masked) { AdditionalProperties = text.AdditionalProperties };

            case FunctionResultContent result when result.Result is not null:
                string raw = result.Result as string ?? JsonSerializer.Serialize(result.Result, AIJsonUtilities.DefaultOptions);
                string maskedResult = MaskJsonText(raw);

                return maskedResult == raw
                    ? content
                    : new FunctionResultContent(result.CallId, maskedResult) { Exception = result.Exception, AdditionalProperties = result.AdditionalProperties };

            default:
                return content;
        }
    }

    /// <summary>Nel JSON serializzato il segno + di un telefono può comparire come <c>+</c>.</summary>
    private static string MaskJsonText(string json) => Mask(json.Replace("\\u002B", "+", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<label>\b(?:tel|telefono|phone|cell|cellulare|mobile|fax)\b\.?:?\s*)\+?\d[\d\s./-]{5,17}\d", RegexOptions.IgnoreCase)]
    private static partial Regex LabelledPhonePattern();

    [GeneratedRegex(@"(?<![\w+])\+\d{1,3}[\s./-]?\d[\d\s./-]{5,14}\d")]
    private static partial Regex InternationalPhonePattern();
}
