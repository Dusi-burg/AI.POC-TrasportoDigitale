using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dusiburg.AI.Trasporto.Documents.Extraction;

/// <summary>Il file non è un PDF leggibile, oppure non contiene testo estraibile (per esempio una scansione).</summary>
public sealed class PdfNotReadableException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <param name="Text">Il testo del documento, pagina dopo pagina, annotazioni comprese.</param>
/// <param name="Annotations">
/// Le annotazioni aggiunte sopra lo stampato, una per elemento: nei CMR sintetici sono le note "a mano", rese in corsivo
/// colorato. Fanno già parte di <paramref name="Text"/>; qui sono isolate perché il codice possa confrontarle con i valori letti.
/// </param>
public sealed record PdfText(string Text, int Pages, IReadOnlyList<string> Annotations);

/// <summary>
/// Legge il testo di un PDF con PdfPig. Segue l'ordine in cui il documento è stato scritto, non la posizione sulla pagina: in
/// un modulo a caselle il contenuto di una casella resta insieme, invece di mescolarsi riga per riga con la casella accanto.
/// Riconosce come annotazione il testo scritto in un colore diverso dal nero e dal grigio dello stampato. Niente OCR: un PDF
/// di sole immagini non ha testo e viene rifiutato, e una nota scritta davvero a penna su una scansione non si vedrebbe.
/// </summary>
public static class PdfTextReader
{
    /// <summary>Separatore fra due testi sulla stessa riga ma distanti: di solito due celle di una tabella.</summary>
    public const string CellSeparator = " | ";

    /// <summary>Quanto possono differire i tre canali di un colore perché sia ancora un grigio.</summary>
    private const double GrayTolerance = 0.08;

    public static PdfText Read(byte[] pdf)
    {
        try
        {
            using PdfDocument document = PdfDocument.Open(pdf);

            var text = new StringBuilder();
            List<string> annotations = [];

            foreach (Page page in document.GetPages())
            {
                AppendPage(text, annotations, page);
                text.Append('\n');
            }

            string result = Tidy(text.ToString());

            return result.Length == 0
                ? throw new PdfNotReadableException("Il PDF non contiene testo estraibile: le scansioni non sono supportate.")
                : new PdfText(result, document.NumberOfPages, [.. annotations.Select(Tidy).Where(a => a.Length > 0)]);
        }
        catch (Exception exception) when (exception is not PdfNotReadableException)
        {
            throw new PdfNotReadableException("Il file non è un PDF leggibile.", exception);
        }
    }

    private static void AppendPage(StringBuilder text, List<string> annotations, Page page)
    {
        Letter? previous = null;
        var annotation = new StringBuilder();

        foreach (Letter letter in page.Letters)
        {
            string separator = previous is null ? string.Empty : SeparatorBetween(previous, letter);
            bool annotated = IsAnnotation(letter);

            text.Append(separator).Append(letter.Value);

            if (annotated)
            {
                // Un'annotazione che va a capo resta una sola nota: l'a capo dentro la nota diventa uno spazio.
                annotation.Append(annotation.Length == 0 ? string.Empty : separator == "\n" ? " " : separator).Append(letter.Value);
            }
            else if (annotation.Length > 0)
            {
                annotations.Add(annotation.ToString());
                annotation.Clear();
            }

            previous = letter;
        }

        if (annotation.Length > 0)
        {
            annotations.Add(annotation.ToString());
        }
    }

    private static string SeparatorBetween(Letter previous, Letter letter)
    {
        double size = Math.Max(previous.PointSize, 1);
        double vertical = Math.Abs(letter.StartBaseLine.Y - previous.StartBaseLine.Y);
        double gap = letter.StartBaseLine.X - previous.EndBaseLine.X;

        return vertical > size * 0.5 || gap < -size ? "\n"
            : gap > size * 2 ? CellSeparator
            : gap > size * 0.2 ? " "
            : string.Empty;
    }

    /// <summary>Lo stampato è nero o grigio; ciò che ha un colore è stato aggiunto sopra.</summary>
    private static bool IsAnnotation(Letter letter)
    {
        if (letter.Color is null || string.IsNullOrWhiteSpace(letter.Value))
        {
            return false;
        }

        (double red, double green, double blue) = letter.Color.ToRGBValues();

        return Math.Abs(red - green) > GrayTolerance || Math.Abs(green - blue) > GrayTolerance || Math.Abs(red - blue) > GrayTolerance;
    }

    /// <summary>Spazi multipli ridotti a uno, righe vuote e spazi ai bordi tolti.</summary>
    private static string Tidy(string text)
    {
        IEnumerable<string> lines = text
            .Split('\n')
            .Select(line => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(line => line.Length > 0);

        return string.Join('\n', lines);
    }
}
