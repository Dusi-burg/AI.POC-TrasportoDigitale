using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dusiburg.AI.Trasporto.Documents.Extraction;

/// <summary>Il file non è un PDF leggibile, oppure non contiene testo estraibile (per esempio una scansione).</summary>
public sealed class PdfNotReadableException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <param name="Text">Il testo del documento, pagina dopo pagina.</param>
public sealed record PdfText(string Text, int Pages);

/// <summary>
/// Legge il testo di un PDF con PdfPig. Segue l'ordine in cui il documento è stato scritto, non la posizione sulla pagina: in
/// un modulo a caselle il contenuto di una casella resta insieme, invece di mescolarsi riga per riga con la casella accanto.
/// Niente OCR: un PDF di sole immagini non ha testo e viene rifiutato.
/// </summary>
public static class PdfTextReader
{
    /// <summary>Separatore fra due testi sulla stessa riga ma distanti: di solito due celle di una tabella.</summary>
    public const string CellSeparator = " | ";

    public static PdfText Read(byte[] pdf)
    {
        try
        {
            using PdfDocument document = PdfDocument.Open(pdf);

            var text = new StringBuilder();

            foreach (Page page in document.GetPages())
            {
                AppendPage(text, page);
                text.Append('\n');
            }

            string result = Tidy(text.ToString());

            return result.Length == 0
                ? throw new PdfNotReadableException("Il PDF non contiene testo estraibile: le scansioni non sono supportate.")
                : new PdfText(result, document.NumberOfPages);
        }
        catch (Exception exception) when (exception is not PdfNotReadableException)
        {
            throw new PdfNotReadableException("Il file non è un PDF leggibile.", exception);
        }
    }

    private static void AppendPage(StringBuilder text, Page page)
    {
        Letter? previous = null;

        foreach (Letter letter in page.Letters)
        {
            if (previous is not null)
            {
                double size = Math.Max(previous.PointSize, 1);
                double vertical = Math.Abs(letter.StartBaseLine.Y - previous.StartBaseLine.Y);
                double gap = letter.StartBaseLine.X - previous.EndBaseLine.X;

                if (vertical > size * 0.5 || gap < -size)
                {
                    text.Append('\n');
                }
                else if (gap > size * 2)
                {
                    text.Append(CellSeparator);
                }
                else if (gap > size * 0.2)
                {
                    text.Append(' ');
                }
            }

            text.Append(letter.Value);
            previous = letter;
        }
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
